using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace MyBudget.Telegram.Presentation;

/// <summary>
/// The only place in the codebase that talks to the Telegram Bot API.
/// </summary>
internal sealed class TelegramSender(
    ITelegramBotClient botClient,
    ILogger<TelegramSender> logger) : ITelegramSender
{
    public async Task SendAsync(
        long chatId, IReadOnlyList<BotResponse> responses, CancellationToken cancellationToken = default)
    {
        var target = new ChatId(chatId);

        foreach (var response in responses)
        {
            await SendOneAsync(target, response, cancellationToken);
        }
    }

    public async Task AcknowledgeCallbackAsync(
        string callbackQueryId, string? notification = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await botClient.AnswerCallbackQuery(
                callbackQueryId, notification, cancellationToken: cancellationToken);
        }
        catch (ApiRequestException exception)
        {
            // An expired callback query is normal and cosmetic; it must not fail the update.
            logger.LogWarning(
                "TelegramCallbackAcknowledgementFailed {ErrorCode}", exception.ErrorCode);
        }
    }

    private async Task SendOneAsync(ChatId chatId, BotResponse response, CancellationToken cancellationToken)
    {
        var text = TelegramHtml.Escape(response.Text);
        var keyboard = RenderKeyboard(response.Keyboard);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await botClient.SendMessage(
                    chatId,
                    text,
                    parseMode: ParseMode.Html,
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken);
                return;
            }
            catch (ApiRequestException exception)
                when (exception.ErrorCode == 429 && attempt < TelegramRetryPolicy.MaxAttempts)
            {
                var delay = TelegramRetryPolicy.DelayFor(exception.Parameters?.RetryAfter);
                logger.LogWarning(
                    "TelegramRateLimited {RetryAfterSeconds} {Attempt}", delay.TotalSeconds, attempt);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static ReplyMarkup? RenderKeyboard(BotKeyboard? keyboard)
    {
        if (keyboard is null)
        {
            return null;
        }

        if (keyboard.Kind == BotKeyboardKind.Reply)
        {
            return new ReplyKeyboardMarkup
            {
                Keyboard = keyboard.Rows
                    .Select(row => row.Select(button => new KeyboardButton(button.Text)).ToArray())
                    .ToArray(),
                ResizeKeyboard = true,
                IsPersistent = true,
            };
        }

        return new InlineKeyboardMarkup(
            keyboard.Rows
                .Select(row => row
                    .Select(button => InlineKeyboardButton.WithCallbackData(
                        button.Text, button.CallbackData ?? string.Empty))
                    .ToArray())
                .ToArray());
    }
}

/// <summary>
/// Used when no bot token is configured. The service still runs for migrations and health
/// checks, and nothing pretends to have been delivered.
/// </summary>
internal sealed class NullTelegramSender(ILogger<NullTelegramSender> logger) : ITelegramSender
{
    public Task SendAsync(
        long chatId, IReadOnlyList<BotResponse> responses, CancellationToken cancellationToken = default)
    {
        logger.LogWarning("TelegramSendSkipped Telegram is not configured");
        return Task.CompletedTask;
    }

    public Task AcknowledgeCallbackAsync(
        string callbackQueryId, string? notification = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
