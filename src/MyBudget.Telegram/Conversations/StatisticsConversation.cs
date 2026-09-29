using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Charts;
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
    private const string ChartCategoriesCallback = CallbackPrefix + "chart-cat";
    private const string ChartDailyCallback = CallbackPrefix + "chart-daily";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        BuildAsync(context, new ReportingPayload(), cancellationToken);

    public Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken) =>
        BuildAsync(context, ReportingPayload.Parse(context.Conversation?.Payload), cancellationToken);

    public async Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var payload = ReportingPayload.Parse(context.Conversation?.Payload);
        var current = payload.Period ?? CurrentPeriod(context);

        // The charts reuse the period on screen, so the buttons keep working after a photo.
        if (callback.Data is ChartCategoriesCallback or ChartDailyCallback)
        {
            return await BuildChartAsync(
                context, current, payload, callback.Data == ChartCategoriesCallback, cancellationToken);
        }

        var target = callback.Data switch
        {
            PreviousCallback => MonthNavigation.Previous(current) ?? current,
            NextCallback => MonthNavigation.Next(current) ?? current,
            _ => current,
        };

        return await BuildAsync(context, payload.WithPeriod(target), cancellationToken);
    }

    private async Task<ConversationTurn> BuildAsync(
        ConversationContext context, ReportingPayload payload, CancellationToken cancellationToken)
    {
        var language = context.Language;
        var period = payload.Period ?? CurrentPeriod(context);
        var statistics = await reports.GetStatisticsAsync(
            context.User.Id, period, Today(context), cancellationToken);

        var turn = new ConversationTurn(
        [
            BotResponse.Message(Render(context, statistics), Keyboard(context, statistics, period)),
        ])
        {
            NextState = State,
            NextPayload = payload.WithPeriod(period).Serialize(),
        };

        return turn;
    }

    /// <summary>
    /// Renders one chart for the period on screen and sends it with the same navigation, so
    /// the user can move months and ask for another chart without going back.
    /// </summary>
    private async Task<ConversationTurn> BuildChartAsync(
        ConversationContext context,
        MonthPeriod period,
        ReportingPayload payload,
        bool byCategory,
        CancellationToken cancellationToken)
    {
        var statistics = await reports.GetStatisticsAsync(
            context.User.Id, period, Today(context), cancellationToken);

        if (statistics.Total == 0)
        {
            // Nothing to draw: the text screen already says the month is empty.
            return await BuildAsync(context, payload.WithPeriod(period), cancellationToken);
        }

        byte[] png;
        string caption;

        if (byCategory)
        {
            (png, caption) = await BuildCategoryChartAsync(context, period, cancellationToken);
        }
        else
        {
            var entries = statistics.Daily
                .Select(day => new ChartEntry(
                    day.Total,
                    day.Date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();

            png = SpendingChartRenderer.VerticalBars(entries);
            caption = messages.Get(
                context.Language, MessageKeys.StatisticsChartDailyCaption, MonthLabel(context, period));
        }

        return new ConversationTurn(
        [
            BotResponse.WithPhoto(png, caption, Keyboard(context, statistics, period)),
        ])
        {
            NextState = State,
            NextPayload = payload.WithPeriod(period).Serialize(),
        };
    }

    /// <summary>
    /// The category bars are numbered and the amount and usage stay on the bar; the category
    /// names, which the bitmap font cannot draw, go in a numbered legend under the chart.
    /// </summary>
    private async Task<(byte[] Png, string Caption)> BuildCategoryChartAsync(
        ConversationContext context,
        MonthPeriod period,
        CancellationToken cancellationToken)
    {
        var language = context.Language;
        var currency = context.User.Currency;
        var summary = await reports.GetMonthlySummaryAsync(
            context.User.Id, period, cancellationToken);

        var lines = summary.Lines
            .Where(line => line.Spent > 0)
            .OrderByDescending(line => line.Spent)
            .ThenBy(line => line.CategoryName, StringComparer.Ordinal)
            .Take(SpendingChartRenderer.MaxHorizontalBars)
            .ToList();

        var entries = new List<ChartEntry>();
        var legend = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var number = index + 1;
            var spent = moneyFormatter.Format(line.Spent, currency);
            var label = $"{line.Icon} {line.CategoryName}";

            if (line.HasBudget)
            {
                var budget = moneyFormatter.Format(line.Budget, currency);
                var usage = moneyFormatter.FormatPercentage(line.UsagePercentage ?? 0m);
                entries.Add(new ChartEntry(line.Spent, $"{number} {spent}/{budget} {usage}"));
                legend.Add(messages.Get(
                    language, MessageKeys.StatisticsChartCategoryLegend, number, label, spent, budget, usage));
            }
            else
            {
                entries.Add(new ChartEntry(line.Spent, $"{number} {spent}"));
                legend.Add(messages.Get(
                    language, MessageKeys.StatisticsChartCategoryLegendNoBudget, number, label, spent));
            }
        }

        var caption = messages.Get(
            language, MessageKeys.StatisticsChartCategoriesCaption, MonthLabel(context, period));

        if (legend.Count > 0)
        {
            caption += "\n\n" + string.Join("\n", legend);
        }

        return (SpendingChartRenderer.HorizontalBars(entries), caption);
    }

    /// <summary>
    /// The navigation row plus the chart buttons. A month with no spending has nothing to
    /// draw, so it shows only the navigation instead of buttons that lead to an empty image.
    /// </summary>
    private BotKeyboard Keyboard(ConversationContext context, PeriodStatistics statistics, MonthPeriod period)
    {
        var navigation = MonthNavigation.Build(
            messages, context.Language, period, PreviousCallback, CurrentCallback, NextCallback);

        var rows = new List<BotButton[]> { navigation.ToArray() };

        if (statistics.Total > 0)
        {
            rows.Add(
            [
                new BotButton(
                    messages.Get(context.Language, MessageKeys.StatisticsButtonCategoriesChart),
                    ChartCategoriesCallback),
                new BotButton(
                    messages.Get(context.Language, MessageKeys.StatisticsButtonDailyChart),
                    ChartDailyCallback),
            ]);
        }

        return BotKeyboard.Inline([.. rows]);
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
