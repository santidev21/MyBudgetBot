using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The monthly statistics: total and average, share per category, daily spending, the biggest
/// expenses and a comparison with the previous month.
/// <para>
/// The comparison is the one place a number can mislead, so a month still in progress says so
/// instead of pretending the total is final. A period with no spending shows an empty report
/// rather than a wall of zeroes.
/// </para>
/// </summary>
internal sealed class StatisticsConversation(
    IUserMessages messages,
    IReportService reports,
    IMoneyFormatter moneyFormatter,
    IUserLocalDate localDate) : IConversation
{
    public const string ConversationName = "statistics";

    private const string State = "statistics";
    private const string CallbackPrefix = "stats:";
    private const string PreviousCallback = CallbackPrefix + "prev";
    private const string NextCallback = CallbackPrefix + "next";
    private const string CurrentCallback = CallbackPrefix + "current";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        BuildAsync(context, new ReportingPayload(), cancellationToken);

    public Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken) =>
        BuildAsync(context, ReportingPayload.Parse(context.Conversation?.Payload), cancellationToken);

    public Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var payload = ReportingPayload.Parse(context.Conversation?.Payload);
        var current = payload.Period ?? CurrentPeriod(context);

        var target = callback.Data switch
        {
            PreviousCallback => MonthNavigation.Previous(current) ?? current,
            NextCallback => MonthNavigation.Next(current) ?? current,
            _ => current,
        };

        return BuildAsync(context, payload.WithPeriod(target), cancellationToken);
    }

    private async Task<ConversationTurn> BuildAsync(
        ConversationContext context, ReportingPayload payload, CancellationToken cancellationToken)
    {
        var language = context.Language;
        var period = payload.Period ?? CurrentPeriod(context);
        var statistics = await reports.GetStatisticsAsync(
            context.User.Id, period, Today(context), cancellationToken);

        var navigation = MonthNavigation.Build(
            messages, language, period, PreviousCallback, CurrentCallback, NextCallback);

        var turn = new ConversationTurn(
        [
            BotResponse.Message(Render(context, statistics), BotKeyboard.Inline([.. navigation])),
        ])
        {
            NextState = State,
            NextPayload = payload.WithPeriod(period).Serialize(),
        };

        return turn;
    }

    private string Render(ConversationContext context, PeriodStatistics statistics)
    {
        var language = context.Language;
        var currency = context.User.Currency;

        var lines = new List<string>
        {
            messages.Get(
                language, MessageKeys.StatisticsHeader, MonthLabel(context, statistics.Period)),
            string.Empty,
            messages.Get(
                language, MessageKeys.StatisticsTotal, moneyFormatter.Format(statistics.Total, currency)),
            messages.Get(
                language,
                MessageKeys.StatisticsExpenseCount,
                statistics.ExpenseCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            messages.Get(
                language,
                MessageKeys.StatisticsAverageDaily,
                moneyFormatter.Format(statistics.AverageDaily, currency)),
        };

        if (statistics.Comparison.PeriodIncomplete)
        {
            lines.Add(messages.Get(language, MessageKeys.StatisticsInProgress));
        }

        if (statistics.Total == 0)
        {
            lines.Add(string.Empty);
            lines.Add(messages.Get(language, MessageKeys.StatisticsEmpty));
            return string.Join("\n", lines);
        }

        AppendCategories(context, lines, statistics);
        AppendDaily(context, lines, statistics);
        AppendLargest(context, lines, statistics);
        AppendComparison(context, lines, statistics);

        return string.Join("\n", lines);
    }

    private void AppendCategories(
        ConversationContext context, List<string> lines, PeriodStatistics statistics)
    {
        if (statistics.Categories.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(messages.Get(context.Language, MessageKeys.StatisticsByCategoryHeader));

        foreach (var category in statistics.Categories)
        {
            lines.Add(messages.Get(
                context.Language,
                MessageKeys.StatisticsCategoryLine,
                $"{category.Icon} {category.CategoryName}",
                moneyFormatter.Format(category.Spent, context.User.Currency),
                moneyFormatter.FormatPercentage(category.SharePercentage)));
        }
    }

    private void AppendDaily(
        ConversationContext context, List<string> lines, PeriodStatistics statistics)
    {
        if (statistics.Daily.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(messages.Get(context.Language, MessageKeys.StatisticsDailyHeader));

        foreach (var day in statistics.Daily)
        {
            lines.Add(messages.Get(
                context.Language,
                MessageKeys.StatisticsDailyLine,
                ShortDate(day.Date),
                moneyFormatter.Format(day.Total, context.User.Currency)));
        }
    }

    private void AppendLargest(
        ConversationContext context, List<string> lines, PeriodStatistics statistics)
    {
        if (statistics.Largest.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(messages.Get(context.Language, MessageKeys.StatisticsLargestHeader));

        foreach (var expense in statistics.Largest)
        {
            var description = string.IsNullOrWhiteSpace(expense.Description)
                ? messages.Get(context.Language, MessageKeys.ExpenseNoDescription)
                : expense.Description;

            lines.Add(messages.Get(
                context.Language,
                MessageKeys.StatisticsLargestLine,
                moneyFormatter.Format(expense.Amount, context.User.Currency),
                description,
                ShortDate(expense.ExpenseDate)));
        }
    }

    private void AppendComparison(
        ConversationContext context, List<string> lines, PeriodStatistics statistics)
    {
        var language = context.Language;
        var currency = context.User.Currency;
        var comparison = statistics.Comparison;
        var previousLabel = MonthLabel(context, comparison.Previous);

        lines.Add(string.Empty);
        lines.Add(messages.Get(language, MessageKeys.StatisticsComparisonHeader));
        lines.Add(messages.Get(
            language,
            MessageKeys.StatisticsComparisonLine,
            previousLabel,
            moneyFormatter.Format(comparison.PreviousTotal, currency)));
        lines.Add(messages.Get(
            language,
            MessageKeys.StatisticsComparisonLine,
            MonthLabel(context, comparison.Period),
            moneyFormatter.Format(comparison.Total, currency)));

        lines.Add(ChangeLine(context, comparison, previousLabel));
    }

    private string ChangeLine(
        ConversationContext context, PeriodComparison comparison, string previousLabel)
    {
        var language = context.Language;

        if (comparison.ChangePercentage is not { } change)
        {
            return messages.Get(language, MessageKeys.StatisticsChangeUnknown, previousLabel);
        }

        if (comparison.Difference == 0)
        {
            return messages.Get(language, MessageKeys.StatisticsChangeFlat, previousLabel);
        }

        var percentage = moneyFormatter.FormatPercentage(Math.Abs(change));
        return comparison.Difference > 0
            ? messages.Get(language, MessageKeys.StatisticsChangeUp, percentage, previousLabel)
            : messages.Get(language, MessageKeys.StatisticsChangeDown, percentage, previousLabel);
    }

    private static string ShortDate(DateOnly date) => $"{date.Day}/{date.Month}";

    private DateOnly Today(ConversationContext context) => localDate.Today(context.User.TimeZone);

    private MonthPeriod CurrentPeriod(ConversationContext context) => MonthPeriod.FromDate(Today(context));

    private string MonthLabel(ConversationContext context, MonthPeriod period) =>
        $"{messages.Get(context.Language, MessageKeys.Months[period.Month - 1])} {period.Year}";
}
