using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The <c>↩️ Deshacer</c> button shown after an expense is registered.
/// <para>
/// It carries only the expense identifier, never any data, and deletes through the
/// ownership-scoped service, so a forged identifier deletes nothing.
/// </para>
/// </summary>
internal sealed class ExpenseUndoHandler(
    IExpenseService expenses,
    IUserMessages messages,
    MainMenu menu) : IGlobalCallback
{
    public const string Prefix = "v1|undo|";

    public async Task<ConversationTurn?> TryHandleAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var data = callback.Data;

        if (!data.StartsWith(Prefix, StringComparison.Ordinal)
            || !Guid.TryParse(data[Prefix.Length..], out var expenseId))
        {
            return null;
        }

        var deleted = await expenses.DeleteAsync(context.User.Id, expenseId, cancellationToken);
        var key = deleted ? MessageKeys.ExpenseUndone : MessageKeys.ExpenseNotFound;

        return new ConversationTurn(
        [
            BotResponse.Message(
                messages.Get(context.Language, key),
                menu.ReplyKeyboard(context.Language)),
        ])
        {
            Completed = true,
        };
    }
}
