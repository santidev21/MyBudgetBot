using System.Globalization;
using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// One category's movements for a month: pick a category, then see each expense it holds.
/// <para>
/// The list is read-only. Opening a movement hands the expense id to the expenses flow, which
/// already owns the detail screen, editing and deletion, so there is no second implementation to
/// keep in step.
/// </para>
/// </summary>
internal sealed class CategoryDetailConversation(
    IUserMessages messages,
    IReportService reports,
    IMoneyFormatter moneyFormatter,
    IUserLocalDate localDate) : IConversation
{
    public const string ConversationName = "category-detail";

    /// <summary>Callback the summary uses to open this flow.</summary>
    public const string OpenCallback = "catdetail:open";

    private const string CallbackPrefix = "catdetail:";
    private const string CategoryPrefix = CallbackPrefix + "cat:";
    private const string ExpensePrefix = CallbackPrefix + "exp:";
    private const string BackCallback = CallbackPrefix + "back";

    /// <summary>Leaves the breakdown for the summary it was opened from.</summary>
    internal const string BackToSummaryCallback = CallbackPrefix + "summary";

    private const string ChooseState = "choose";
    private const string MovementsState = "movements";
    private const int PageSize = 20;

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        BuildChooserAsync(context, cancellationToken);

    // The breakdown only owns its navigation callbacks: free text passes through it.
    public Task<ConversationTurn?> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken) =>
        Task.FromResult<ConversationTurn?>(null);

    public async Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var period = CurrentPeriod(context);

        if (callback.Data == BackToSummaryCallback)
        {
            // The breakdown is a side screen of the summary; going back resumes it.
            return new ConversationTurn([])
            {
                HandoffConversation = SummaryConversation.ConversationName,
            };
        }

        if (callback.Data.StartsWith(CategoryPrefix, StringComparison.Ordinal)
            && Guid.TryParse(callback.Data[CategoryPrefix.Length..], out var categoryId))
        {
            return await BuildMovementsAsync(context, period, categoryId, cancellationToken);
        }

        if (callback.Data.StartsWith(ExpensePrefix, StringComparison.Ordinal)
            && Guid.TryParse(callback.Data[ExpensePrefix.Length..], out var expenseId))
        {
            // The expenses flow knows how to render, edit and delete a single expense.
            return new ConversationTurn([])
            {
                HandoffConversation = ExpensesConversation.ConversationName,
                HandoffPayload = expenseId.ToString(),
            };
        }

        return await BuildChooserAsync(context, cancellationToken);
    }

    private async Task<ConversationTurn> BuildChooserAsync(
        ConversationContext context, CancellationToken cancellationToken)
    {
        var language = context.Language;
        var period = CurrentPeriod(context);
        var statistics = await reports.GetStatisticsAsync(
            context.User.Id, period, Today(context), cancellationToken);

        var rows = statistics.Categories
            .Where(category => category.Spent > 0)
            .Select(category => new[]
            {
                new BotButton(
                    $"{category.Icon} {category.CategoryName} — {moneyFormatter.Format(category.Spent, context.User.Currency)}",
                    CategoryPrefix + category.CategoryId),
            })
            .ToList();

        var text = rows.Count == 0
            ? messages.Get(language, MessageKeys.BreakdownEmpty)
            : messages.Get(language, MessageKeys.BreakdownChoose);

        // Without this row the only way out of the breakdown is the persistent menu.
        rows.Add([new BotButton(messages.Get(language, MessageKeys.ButtonBack), BackToSummaryCallback)]);

        return new ConversationTurn([BotResponse.Message(text, BotKeyboard.Inline([.. rows]))])
        {
            NextState = ChooseState,
            NextPayload = "{}",
        };
    }

    private async Task<ConversationTurn> BuildMovementsAsync(
        ConversationContext context, MonthPeriod period, Guid categoryId,
        CancellationToken cancellationToken)
    {
        var language = context.Language;
        var currency = context.User.Currency;
        var statistics = await reports.GetStatisticsAsync(
            context.User.Id, period, Today(context), cancellationToken);
        var category = statistics.Categories.FirstOrDefault(share => share.CategoryId == categoryId);

        var history = await reports.GetHistoryAsync(
            context.User.Id, DateRange.ForMonth(period), after: null, pageSize: PageSize,
            categoryId: categoryId, cancellationToken: cancellationToken);

        var header = category is null
            ? MonthLabel(context, period)
            : messages.Get(
                language,
                MessageKeys.BreakdownHeader,
                $"{category.Icon} {category.CategoryName}",
                MonthLabel(context, period));

        var lines = new List<string> { header, string.Empty };

        if (history.Items.Count == 0)
        {
            lines.Add(messages.Get(language, MessageKeys.BreakdownEmpty));
        }
        else
        {
            foreach (var item in history.Items)
            {
                var description = string.IsNullOrWhiteSpace(item.Description)
                    ? messages.Get(language, MessageKeys.ExpenseNoDescription)
                    : item.Description;

                lines.Add(messages.Get(
                    language,
                    MessageKeys.BreakdownLine,
                    ShortDate(item.ExpenseDate),
                    description,
                    moneyFormatter.Format(item.Amount, currency)));
            }

            lines.Add(string.Empty);
            lines.Add(messages.Get(
                language,
                MessageKeys.BreakdownTotal,
                moneyFormatter.Format(history.Items.Sum(item => item.Amount), currency),
                history.Items.Count.ToString(CultureInfo.InvariantCulture)));
        }

        var movementButtons = history.Items
            .Select(item => new[]
            {
                new BotButton(
                    $"{ShortDate(item.ExpenseDate)} · {moneyFormatter.Format(item.Amount, currency)}",
                    ExpensePrefix + item.Id),
            })
            .ToList();
        movementButtons.Add([new BotButton(messages.Get(language, MessageKeys.CategoryButtonBack), BackCallback)]);

        return new ConversationTurn(
        [
            BotResponse.Message(string.Join("\n", lines), BotKeyboard.Inline([.. movementButtons])),
        ])
        {
            NextState = MovementsState,
            NextPayload = "{}",
        };
    }

    private static string ShortDate(DateOnly date) =>
        date.ToString("dd/MM", CultureInfo.InvariantCulture);

    private DateOnly Today(ConversationContext context) =>
        localDate.Today(context.User.TimeZone);

    private MonthPeriod CurrentPeriod(ConversationContext context) =>
        MonthPeriod.FromDate(Today(context));

    private string MonthLabel(ConversationContext context, MonthPeriod period) =>
        $"{messages.Get(context.Language, MessageKeys.Months[period.Month - 1])} {period.Year}";
}
