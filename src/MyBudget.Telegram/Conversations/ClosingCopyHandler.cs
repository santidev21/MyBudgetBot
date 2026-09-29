using MyBudget.Application.Budgets;
using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Presentation;
using MyBudget.Telegram.Reporting;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The "copy last month's budget" button of a monthly closing.
/// <para>
/// It is a global callback because the closing message outlives any conversation. The data
/// carries only the closed month; the copy runs through the ownership-scoped budget service, so
/// a forged payload can at most copy the caller's own budget.
/// </para>
/// </summary>
internal sealed class ClosingCopyHandler(
    IBudgetService budgets,
    IUserLocalDate localDate,
    IUserMessages messages,
    MainMenu menu) : IGlobalCallback
{
    public async Task<ConversationTurn?> TryHandleAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        if (!ClosingCopyCallback.TryParse(callback.Data, out var closedPeriod))
        {
            return null;
        }

        // The new month is the one after the closing; CopyPreviousMonthAsync copies the closed
        // period into it. Today comes from the user's own time zone, like every month rule.
        var newPeriod = closedPeriod.Next;
        var today = localDate.Today(context.User.TimeZone);

        var result = await budgets.CopyPreviousMonthAsync(
            context.User.Id, newPeriod, today, cancellationToken);

        var key = result.Status switch
        {
            BudgetCopyStatus.Copied => MessageKeys.BudgetCopied,
            BudgetCopyStatus.NoPreviousBudget => MessageKeys.BudgetNoPrevious,
            _ => MessageKeys.BudgetPastMonth,
        };

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
