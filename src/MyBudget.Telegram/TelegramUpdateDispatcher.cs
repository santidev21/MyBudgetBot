using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Localization;
using MyBudget.Application.Users;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.Presentation;
using MyBudget.Telegram.RateLimiting;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MyBudget.Telegram;

public interface ITelegramUpdateDispatcher
{
    /// <summary>
    /// Processes one update. Returns normally for anything that should not be retried, and
    /// throws only when a retry could help.
    /// </summary>
    Task DispatchAsync(Update update, CancellationToken cancellationToken = default);
}

/// <summary>
/// The pipeline every Telegram update goes through.
/// <para>
/// Order matters: claim the update, gate it, resolve the user, run the work under a per-user
/// lock, then send. The reply is sent only after the state change is committed, so a crash can
/// lose a reply but never a financial record.
/// </para>
/// </summary>
internal sealed class TelegramUpdateDispatcher(
    IUpdateInbox inbox,
    IUserService users,
    IUserWorkLock userWorkLock,
    IConversationStore conversations,
    ConversationRouter router,
    ITelegramSender sender,
    IUserMessages messages,
    IUserRateLimiter rateLimiter,
    IOptions<TelegramOptions> options,
    TimeProvider timeProvider,
    ILogger<TelegramUpdateDispatcher> logger) : ITelegramUpdateDispatcher
{
    public async Task DispatchAsync(Update update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var updateId = (long)update.Id;
        logger.LogInformation(
            "TelegramUpdateReceived {UpdateId} {UpdateType}", updateId, update.Type);

        if (!await inbox.TryClaimAsync(updateId, cancellationToken))
        {
            // Telegram retried something already handled. Answering 200 without acting is the
            // whole point of the inbox.
            logger.LogInformation(
                "TelegramUpdateSkipped {UpdateId} {Reason}", updateId, "already-processed");
            return;
        }

        try
        {
            await ProcessAsync(update, updateId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Only the exception type is recorded: log bodies can contain user text and,
            // through it, financial detail.
            logger.LogError(exception, "TelegramUpdateFailed {UpdateId}", updateId);
            await inbox.FailAsync(updateId, exception.GetType().Name, CancellationToken.None);
            throw;
        }
    }

    private async Task ProcessAsync(Update update, long updateId, CancellationToken cancellationToken)
    {
        var from = update.Message?.From ?? update.CallbackQuery?.From;
        var chat = update.Message?.Chat ?? update.CallbackQuery?.Message?.Chat;

        if (from is null || chat is null)
        {
            await inbox.IgnoreAsync(updateId, null, "unsupported-update", cancellationToken);
            return;
        }

        if (chat.Type != ChatType.Private)
        {
            // The bot is personal; group chats would let anyone spend someone else's ledger.
            await inbox.IgnoreAsync(updateId, null, "not-a-private-chat", cancellationToken);
            return;
        }

        if (!options.Value.IsUserAllowed(from.Id))
        {
            logger.LogWarning(
                "TelegramUpdateBlocked {UpdateId} {TelegramUserId}", updateId, from.Id);

            // An unknown user has no language yet; the catalog falls back to the configured
            // default, so the refusal is still in a language the user might read.
            await sender.SendAsync(
                chat.Id, [BotResponse.Message(messages.Get(string.Empty, MessageKeys.AccessDenied))],
                cancellationToken);

            await inbox.IgnoreAsync(updateId, null, "not-allowlisted", cancellationToken);
            return;
        }

        if (IsStale(update))
        {
            // Ignored silently on purpose: after downtime Telegram replays queued updates, and
            // answering each one would flood the user with explanations.
            logger.LogInformation("TelegramUpdateSkipped {UpdateId} {Reason}", updateId, "stale");
            await inbox.IgnoreAsync(updateId, null, "stale", cancellationToken);
            return;
        }

        // Checked after the stale gate on purpose: a replay of queued updates must not spend
        // the budget a current message needs, and before the user lookup so refusing is cheap.
        var decision = rateLimiter.Evaluate(from.Id);

        if (decision != RateLimitDecision.Allowed)
        {
            logger.LogWarning(
                "TelegramUpdateThrottled {UpdateId} {TelegramUserId}", updateId, from.Id);

            if (decision == RateLimitDecision.ThrottledAndNotified)
            {
                await sender.SendAsync(
                    chat.Id, [BotResponse.Message(messages.Get(string.Empty, MessageKeys.RateLimited))],
                    cancellationToken);
            }

            await inbox.IgnoreAsync(updateId, null, "rate-limited", cancellationToken);
            return;
        }

        var user = await users.GetOrCreateAsync(
            from.Id, from.Username, BuildDisplayName(from), cancellationToken);

        var text = update.Message?.Text;
        var callbackData = update.CallbackQuery?.Data;

        if (text is null && callbackData is null)
        {
            await inbox.IgnoreAsync(updateId, user.Id, "unsupported-content", cancellationToken);
            return;
        }

        var turn = await userWorkLock.ExecuteAsync(
            user.Id,
            async token =>
            {
                var snapshot = await conversations.FindAsync(user.Id, token);
                var context = new ConversationContext(user, chat.Id, snapshot);

                return callbackData is not null
                    ? await router.RouteCallbackAsync(
                        context, new IncomingCallback(update.CallbackQuery!.Id, callbackData), token)
                    : await router.RouteTextAsync(context, text!, token);
            },
            cancellationToken);

        if (update.CallbackQuery is { } callback)
        {
            // Answered before the messages go out so the button stops spinning immediately.
            // A toast would be nicer, but Telegram only accepts one answer per query.
            await sender.AcknowledgeCallbackAsync(callback.Id, null, cancellationToken);
        }

        await sender.SendAsync(chat.Id, turn.Responses, cancellationToken);
        await inbox.CompleteAsync(updateId, user.Id, cancellationToken);

        logger.LogInformation(
            "TelegramUpdateProcessed {UpdateId} {TelegramUserId}", updateId, from.Id);
    }

    private bool IsStale(Update update)
    {
        var sentAt = update.Message?.Date ?? update.CallbackQuery?.Message?.Date;

        if (sentAt is not { } sent)
        {
            return false;
        }

        var utc = new DateTimeOffset(DateTime.SpecifyKind(sent, DateTimeKind.Utc));
        return timeProvider.GetUtcNow() - utc > options.Value.StaleUpdateWindow;
    }

    private static string? BuildDisplayName(User user)
    {
        var full = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return full.Length == 0 ? null : full;
    }
}
