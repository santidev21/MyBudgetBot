using System.Globalization;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Reporting;

/// <summary>
/// Renders a month's closing report.
/// <para>
/// Text only, with a text bar next to each category and the copy-budget button. The comparison
/// reuses the statistics wording so a change is described the same way everywhere, and a closed
/// month always carries the note that its figures are final.
/// </para>
/// </summary>
internal static class MonthlyClosingMessages
{
    private const int BarLength = 10;
    private const char BarFilled = '▓';
    private const char BarEmpty = '░';

    public static BotResponse Format(
        IUserMessages messages, IMoneyFormatter moneyFormatter, MonthlyClosing closing)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(moneyFormatter);
        ArgumentNullException.ThrowIfNull(closing);

        var language = closing.User.Language;
        var currency = closing.User.Currency;
        var previousLabel = MonthLabel(messages, language, closing.ClosedPeriod.Previous);

        var lines = new List<string>
        {
            messages.Get(language, MessageKeys.MonthlyClosingHeader, MonthLabel(messages, language, closing.ClosedPeriod)),
            string.Empty,
            messages.Get(
                language, MessageKeys.MonthlyClosingTotalSpent, moneyFormatter.Format(closing.TotalSpent, currency)),
            messages.Get(
                language,
                MessageKeys.MonthlyClosingExpenseCount,
                closing.ExpenseCount.ToString(CultureInfo.InvariantCulture)),
            messages.Get(language, MessageKeys.MonthlyClosingCompleteNote),
            string.Empty,
            messages.Get(language, MessageKeys.MonthlyClosingComparisonHeader, previousLabel),
            ChangeLine(messages, moneyFormatter, language, closing, previousLabel),
        };

        if (closing.TopCategories.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(messages.Get(language, MessageKeys.MonthlyClosingTopCategoriesHeader));
            var top = closing.TopCategories.Max(category => category.Spent);

            foreach (var category in closing.TopCategories)
            {
                lines.Add(messages.Get(
                    language,
                    MessageKeys.MonthlyClosingCategoryLine,
                    $"{category.Icon} {category.Name}",
                    moneyFormatter.Format(category.Spent, currency),
                    Bar(top, category.Spent),
                    moneyFormatter.FormatPercentage(category.SharePercentage)));
            }
        }

        if (closing.IsOverBudget)
        {
            lines.Add(string.Empty);
            lines.Add(messages.Get(
                language,
                MessageKeys.MonthlyClosingOverBudget,
                moneyFormatter.Format(closing.TotalSpent, currency),
                moneyFormatter.Format(closing.TotalAllocated, currency)));
        }

        var keyboard = BotKeyboard.Inline(
        [
            new BotButton(
                messages.Get(language, MessageKeys.BudgetButtonCopy),
                ClosingCopyCallback.ForPeriod(closing.ClosedPeriod)),
        ]);

        return BotResponse.Message(string.Join("\n", lines), keyboard);
    }

    private static string ChangeLine(
        IUserMessages messages,
        IMoneyFormatter moneyFormatter,
        string language,
        MonthlyClosing closing,
        string previousLabel)
    {
        if (closing.ChangePercentage is not { } change)
        {
            // The previous month had no spending, so a percentage would be meaningless.
            return messages.Get(language, MessageKeys.StatisticsChangeUnknown, previousLabel);
        }

        if (closing.TotalSpent == closing.PreviousTotal)
        {
            return messages.Get(language, MessageKeys.StatisticsChangeFlat, previousLabel);
        }

        var percentage = moneyFormatter.FormatPercentage(Math.Abs(change));
        return closing.TotalSpent > closing.PreviousTotal
            ? messages.Get(language, MessageKeys.StatisticsChangeUp, percentage, previousLabel)
            : messages.Get(language, MessageKeys.StatisticsChangeDown, percentage, previousLabel);
    }

    private static string Bar(long max, long spent)
    {
        if (max <= 0)
        {
            return string.Empty;
        }

        var filled = (int)Math.Round(spent * BarLength / (decimal)max, MidpointRounding.AwayFromZero);
        filled = Math.Clamp(filled, 0, BarLength);
        return new string(BarFilled, filled) + new string(BarEmpty, BarLength - filled);
    }

    private static string MonthLabel(IUserMessages messages, string language, MonthPeriod period) =>
        $"{messages.Get(language, MessageKeys.Months[period.Month - 1])} {period.Year}";
}
