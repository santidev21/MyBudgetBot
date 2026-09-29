using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The user's own settings: the daily reminder switch and the irreversible erasure of everything
/// they own.
/// <para>
/// Erasure deletes the user row itself, so the next message recreates a clean user. That is why
/// the confirmation is explicit and the turn marks <see cref="ConversationTurn.UserRemoved"/>:
/// the update inbox must not link a settled update to an owner that no longer exists.
/// </para>
/// </summary>
internal sealed class SettingsConversation(
    IUserMessages messages,
    MainMenu menu,
    IUserDataEraser eraser,
    IUnitOfWork unitOfWork) : IConversation
{
    public const string ConversationName = "settings";

    private const string State = "settings";
    private const string ReminderCallback = "settings:reminder";
    private const string DeleteCallback = "settings:delete";
    private const string DeleteConfirmCallback = "settings:delete:confirm";
    private const string BackCallback = "settings:back";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Build(context));

    public Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken) =>
        Task.FromResult(Build(context));

    public async Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        switch (callback.Data)
        {
            case ReminderCallback:
                context.User.ChangeDailyReminder(!context.User.DailyReminderEnabled);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Build(context);

            case DeleteCallback:
                return ConfirmDelete(context);

            case DeleteConfirmCallback:
                await eraser.EraseAsync(context.User.Id, cancellationToken);
                return new ConversationTurn(
                [
                    BotResponse.Message(
                        messages.Get(context.Language, MessageKeys.SettingsDeleted),
                        menu.ReplyKeyboard(context.Language)),
                ])
                {
                    Completed = true,
                    UserRemoved = true,
                };

            default:
                // Both Cancel and anything unrecognised return to the settings screen rather
                // than dropping the user back with no way forward.
                return Build(context);
        }
    }

    private ConversationTurn Build(ConversationContext context)
    {
        var language = context.Language;
        var reminderOn = context.User.DailyReminderEnabled;

        var lines = new[]
        {
            messages.Get(language, MessageKeys.SettingsTitle),
            string.Empty,
            messages.Get(
                language,
                reminderOn ? MessageKeys.SettingsReminderStateOn : MessageKeys.SettingsReminderStateOff),
        };

        var reminderButton = new BotButton(
            messages.Get(
                language,
                reminderOn ? MessageKeys.SettingsReminderDisable : MessageKeys.SettingsReminderEnable),
            ReminderCallback);

        var keyboard = BotKeyboard.Inline(
        [
            [reminderButton],
            [new BotButton(messages.Get(language, MessageKeys.SettingsDeleteData), DeleteCallback)],
        ]);

        return new ConversationTurn([BotResponse.Message(string.Join("\n", lines), keyboard)])
        {
            NextState = State,
            NextPayload = "{}",
        };
    }

    private ConversationTurn ConfirmDelete(ConversationContext context)
    {
        var language = context.Language;

        var keyboard = BotKeyboard.Inline(
        [
            [new BotButton(messages.Get(language, MessageKeys.SettingsDeleteConfirm), DeleteConfirmCallback)],
            [new BotButton(messages.Get(language, MessageKeys.ButtonCancel), BackCallback)],
        ]);

        return new ConversationTurn(
        [
            BotResponse.Message(messages.Get(language, MessageKeys.SettingsDeleteWarning), keyboard),
        ])
        {
            NextState = State,
            NextPayload = "{}",
        };
    }
}
