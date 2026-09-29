using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The monthly summary: what was spent, against each category's historical allocation, with a
/// text bar per category and buttons to walk the months.
/// <para>
/// The period is read from that month's allocation row, so September's report never shows
/// October's budget. A category with spending but no allocation renders a dash rather than a
/// division by zero, and overspending is shown, never blocked.
/// </para>
/// </summary>
internal sealed class SummaryConversation(
    IUserMessages messages,
    IReportService reports,
    IMoneyFormatter moneyFormatter,
    IUserLocalDate localDate) : IConversation
{
    public const string ConversationName = "summary";

    private const string State = "summary";
    private const string CallbackPrefix = "sum:";
    private const string PreviousCallback = CallbackPrefix + "prev";
    private const string NextCallback = CallbackPrefix + "next";
    private const string CurrentCallback = CallbackPrefix + "current";

    private const int BarLength = 10;
    private const char BarFilled = '▓';
    private const char BarEmpty = '░';

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
        if (callback.Data == CategoryDetailConversation.OpenCallback)
        {
            // The category detail is its own flow; the summary hands it the turn.
            return Task.FromResult(new ConversationTurn([])
            {
                HandoffConversation = CategoryDetailConversation.ConversationName,
            });
        }

        var payload = ReportingPayload.Parse(context.Conversation?.Payload);
        var current = payload.Period ?? CurrentPeriod(context);

        var target = callback.Data switch
        {
            PreviousCallback => MonthNavigation.Previous(current) ?? current,
            NextCallback => MonthNavigation.Next(current) ?? current,
            CurrentCallback => current,
            _ => current,
        };

        return BuildAsync(context, payload.WithPeriod(target), cancellationToken);
    }

    private async Task<ConversationTurn> BuildAsync(
        ConversationContext context, ReportingPayload payload, CancellationToken cancellationToken)
    {
        var language = context.Language;
        var period = payload.Period ?? CurrentPeriod(context);
        var summary = await reports.GetMonthlySummaryAsync(context.User.Id, period, cancellationToken);

        var navigation = MonthNavigation.Build(
            messages, language, period, PreviousCallback, CurrentCallback, NextCallback);

        var turn = new ConversationTurn(
        [
            BotResponse.Message(
                Render(context, summary),
                BotKeyboard.Inline(
                [
                    [new BotButton(
                        messages.Get(language, MessageKeys.SummaryButtonByCategory),
                        CategoryDetailConversation.OpenCallback)],
                    [.. navigation],
                ])),
        ])
        {
            NextState = State,
            NextPayload = payload.WithPeriod(period).Serialize(),
        };

        return turn;
    }

    private string Render(ConversationContext context, MonthlySummary summary)
    {
        var language = context.Language;
        var currency = context.User.Currency;
        var lines = new List<string>
        {
            messages.Get(language, MessageKeys.SummaryHeader, MonthLabel(context, summary.Period)),
            string.Empty,
            messages.Get(
                language,
                MessageKeys.SummaryTotalSpent,
                moneyFormatter.Format(summary.TotalSpent, currency)),
        };

        if (summary.TotalAllocated > 0)
        {
            lines.Add(messages.Get(
                language,
                MessageKeys.SummaryTotalBudget,
                moneyFormatter.Format(summary.TotalAllocated, currency),
                moneyFormatter.FormatPercentage(summary.UsagePercentage ?? 0m)));

            var remaining = summary.TotalAllocated - summary.TotalSpent;
            lines.Add(remaining >= 0
                ? messages.Get(language, MessageKeys.SummaryRemaining, moneyFormatter.Format(remaining, currency))
                : messages.Get(
                    language, MessageKeys.SummaryOverspent, moneyFormatter.Format(-remaining, currency)));
        }

        var display = summary.Lines
            .Where(line => line.Spent > 0 || line.HasBudget)
            .ToList();

        if (display.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add(messages.Get(language, MessageKeys.SummaryEmpty));
            return string.Join("\n", lines);
        }

        lines.Add(string.Empty);
        foreach (var line in display)
        {
            lines.Add(RenderLine(context, line));
        }

        return string.Join("\n", lines);
    }

    private string RenderLine(ConversationContext context, BudgetLine line)
    {
        var language = context.Language;
        var currency = context.User.Currency;
        var label = $"{line.Icon} {line.CategoryName}";
        var spent = moneyFormatter.Format(line.Spent, currency);

        if (!line.HasBudget)
        {
            return messages.Get(language, MessageKeys.SummaryLineUnbudgeted, label, spent);
        }

        return messages.Get(
            language,
            MessageKeys.SummaryLine,
            label,
            spent,
            moneyFormatter.Format(line.Budget, currency),
            Bar(line.Budget, line.Spent),
            moneyFormatter.FormatPercentage(line.UsagePercentage ?? 0m));
    }

    private static string Bar(long budget, long spent)
    {
        if (budget <= 0)
        {
            return string.Empty;
        }

        var filled = (int)Math.Round(spent * BarLength / (decimal)budget, MidpointRounding.AwayFromZero);
        filled = Math.Clamp(filled, 0, BarLength);
        return new string(BarFilled, filled) + new string(BarEmpty, BarLength - filled);
    }

    private DateOnly Today(ConversationContext context) => localDate.Today(context.User.TimeZone);

    private MonthPeriod CurrentPeriod(ConversationContext context) => MonthPeriod.FromDate(Today(context));

    private string MonthName(string language, MonthPeriod period) =>
        messages.Get(language, MessageKeys.Months[period.Month - 1]);

    private string MonthLabel(ConversationContext context, MonthPeriod period) =>
        $"{MonthName(context.Language, period)} {period.Year}";
}
