using Microsoft.Extensions.Options;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// Decides which conversation owns an input, runs it, and persists the resulting state.
/// <para>
/// All the flow logic lives in the conversations. This type only routes, and it is the single
/// place where conversation state is written, so a flow can never forget to persist itself.
/// </para>
/// </summary>
internal sealed class ConversationRouter(
    IConversationStore store,
    IUserMessages messages,
    MainMenu menu,
    IEnumerable<IConversation> conversations,
    IEnumerable<IGlobalCallback> globalCallbacks,
    ICompactExpenseParser compactParser,
    IOptions<TelegramOptions> options,
    TimeProvider timeProvider)
{
    public async Task<ConversationTurn> RouteTextAsync(
        ConversationContext context, string text, CancellationToken cancellationToken)
    {
        var command = BotCommands.Parse(text);

        if (command == BotCommands.Cancel)
        {
            await store.ClearAsync(context.User.Id, cancellationToken);
            return Finished(ConversationTurn.Say(messages.Get(context.Language, MessageKeys.Cancelled)));
        }

        if (command is not null)
        {
            return await RunCommandAsync(context, command, cancellationToken);
        }

        // A tap on the persistent menu outranks an in-progress flow. Without this, typing a
        // menu label mid-flow would be swallowed as input by the active conversation (a
        // "📊 Resumen" tap becoming a category name, for example).
        if (menu.MatchAction(context.Language, text) is { } action)
        {
            return await StartMenuActionAsync(context, action, cancellationToken);
        }

        if (ActiveConversation(context) is { } active)
        {
            var turn = await active.HandleTextAsync(context, new IncomingText(text), cancellationToken);
            await PersistAsync(context, active.Name, turn, cancellationToken);
            return turn;
        }

        // Free text with nothing active is a compact entry: "35.000 verduras" becomes the same
        // confirmation. Text that carries no amount is not an entry and falls through to help.
        var compact = compactParser.Parse(text, context.User.Currency);
        if (compact.Outcome != CompactExpenseOutcome.Unparsed
            && Find(ExpenseConversation.ConversationName) is IExpenseEntry entry)
        {
            await store.ClearAsync(context.User.Id, cancellationToken);
            var fresh = context with { Conversation = null };
            var turn = await entry.StartFromCompactAsync(fresh, compact, cancellationToken);
            await PersistAsync(fresh, ExpenseConversation.ConversationName, turn, cancellationToken);
            return turn;
        }

        return Finished(HelpTurn(context));
    }

    public async Task<ConversationTurn> RouteCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        if (ActiveConversation(context) is { } active)
        {
            var turn = await active.HandleCallbackAsync(context, callback, cancellationToken);
            await PersistAsync(context, active.Name, turn, cancellationToken);
            return turn;
        }

        // No conversation is active, so this is a global action such as Undo. A callback
        // nobody recognises is treated as expired rather than silently ignored.
        foreach (var globalAction in globalCallbacks)
        {
            if (await globalAction.TryHandleAsync(context, callback, cancellationToken) is { } handled)
            {
                return handled;
            }
        }

        return Finished(
            ConversationTurn.Say(
                messages.Get(context.Language, MessageKeys.ConversationExpired),
                menu.ReplyKeyboard(context.Language)));
    }

    private async Task<ConversationTurn> RunCommandAsync(
        ConversationContext context, string command, CancellationToken cancellationToken)
    {
        switch (command)
        {
            case BotCommands.Start:
                {
                    // Starting again replaces whatever was in progress.
                    await store.ClearAsync(context.User.Id, cancellationToken);

                    if (Find(StartConversation.ConversationName) is not { } onboarding)
                    {
                        break;
                    }

                    var fresh = context with { Conversation = null };
                    var started = await onboarding.StartAsync(fresh, cancellationToken);
                    await PersistAsync(fresh, onboarding.Name, started, cancellationToken);
                    return started;
                }

            case BotCommands.Help:
                await store.ClearAsync(context.User.Id, cancellationToken);
                return Finished(HelpTurn(context));
        }

        return Finished(HelpTurn(context));
    }

    private async Task<ConversationTurn> StartMenuActionAsync(
        ConversationContext context, string action, CancellationToken cancellationToken)
    {
        var conversationName = action switch
        {
            MessageKeys.MenuCategories => CategoriesConversation.ConversationName,
            MessageKeys.MenuAddExpense => ExpenseConversation.ConversationName,
            MessageKeys.MenuExpenses => ExpensesConversation.ConversationName,
            MessageKeys.MenuRecurring => RecurringConversation.ConversationName,
            MessageKeys.MenuSummary => SummaryConversation.ConversationName,
            MessageKeys.MenuStatistics => StatisticsConversation.ConversationName,
            _ => null,
        };

        // Tapping a menu item abandons whatever was in progress: the menu is the user's way out.
        await store.ClearAsync(context.User.Id, cancellationToken);

        if (conversationName is null || Find(conversationName) is not { } conversation)
        {
            return Finished(
                ConversationTurn.Say(messages.Get(context.Language, MessageKeys.FeatureNotReady)));
        }

        var fresh = context with { Conversation = null };
        var started = await conversation.StartAsync(fresh, cancellationToken);
        await PersistAsync(fresh, conversation.Name, started, cancellationToken);
        return started;
    }

    private IConversation? ActiveConversation(ConversationContext context) =>
        context.Conversation is { } snapshot ? Find(snapshot.Conversation) : null;

    private IConversation? Find(string name) =>
        conversations.FirstOrDefault(conversation => conversation.Name == name);

    private async Task PersistAsync(
        ConversationContext context, string conversationName, ConversationTurn turn,
        CancellationToken cancellationToken)
    {
        if (turn.Completed || turn.NextState is null)
        {
            await store.ClearAsync(context.User.Id, cancellationToken);
            return;
        }

        await store.SaveAsync(
            new ConversationSnapshot(
                context.User.Id,
                context.ChatId,
                conversationName,
                turn.NextState,
                turn.NextPayload ?? "{}",
                timeProvider.GetUtcNow() + options.Value.ConversationTimeout),
            cancellationToken);
    }

    private ConversationTurn HelpTurn(ConversationContext context) =>
        ConversationTurn.Say(
            messages.Get(context.Language, MessageKeys.Help), menu.ReplyKeyboard(context.Language));

    /// <summary>
    /// A turn that ends the flow. The state is cleared by <see cref="PersistAsync"/>, which is
    /// what makes an abandoned flow impossible to resume by accident.
    /// </summary>
    private static ConversationTurn Finished(ConversationTurn turn) =>
        turn with { Completed = true, NextState = null };
}
