using MyBudget.Application.Budgets;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// Renders a crossed budget threshold. Shared by the expense flow and the recurring pass so the
/// wording lives in one place and is never duplicated.
/// </summary>
internal static class BudgetAlertMessages
{
    public static string Format(
        IUserMessages messages,
        IMoneyFormatter moneyFormatter,
        string language,
        string currency,
        BudgetAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var key = alert.Threshold >= BudgetAlertService.OverBudgetThreshold
            ? MessageKeys.BudgetAlertExceeded
            : MessageKeys.BudgetAlertNearLimit;

        return messages.Get(
            language,
            key,
            moneyFormatter.FormatPercentage(alert.UsagePercentage),
            $"{alert.Icon} {alert.CategoryName}",
            moneyFormatter.Format(alert.Spent, currency),
            moneyFormatter.Format(alert.Allocated, currency));
    }

    public static IEnumerable<BotResponse> Render(
        IUserMessages messages,
        IMoneyFormatter moneyFormatter,
        string language,
        string currency,
        IReadOnlyList<BudgetAlert> alerts)
    {
        ArgumentNullException.ThrowIfNull(alerts);

        foreach (var alert in alerts)
        {
            yield return BotResponse.Message(Format(messages, moneyFormatter, language, currency, alert));
        }
    }
}
