using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Budgets;
using MyBudget.Application.Expenses;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;

namespace MyBudget.Application.Reporting;

/// <summary>
/// Period queries for the summary, the history and the statistics screens.
/// <para>
/// All read-only: it composes the SQL aggregations with category labels and derives the totals.
/// A month with no budget yields usage <c>null</c> rather than dividing by zero, and an
/// overspent month is reported as overspent instead of being refused. Nothing here is stored.
/// </para>
/// </summary>
public interface IReportService
{
    Task<MonthlySummary> GetMonthlySummaryAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of the range history, ordered by <c>(expense_date DESC, id DESC)</c>.
    /// <paramref name="after"/> resumes from the previous page; the page size is capped by the
    /// caller.
    /// </summary>
    Task<ExpenseHistoryPage> GetHistoryAsync(
        Guid userId,
        DateRange range,
        ExpensePageCursor? after,
        int pageSize,
        Guid? categoryId = null,
        CancellationToken cancellationToken = default);

    Task<PeriodStatistics> GetStatisticsAsync(
        Guid userId, MonthPeriod period, DateOnly today, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ReportService(
    IExpenseReadRepository expenseQueries,
    IBudgetRepository budgets,
    ICategoryRepository categories) : IReportService
{
    /// <summary>How many of the period's biggest expenses the statistics screen shows.</summary>
    public const int LargestExpenseCount = 5;

    public async Task<MonthlySummary> GetMonthlySummaryAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var range = DateRange.ForMonth(period);
        var budget = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
        var defaults = await budgets.ListDefaultsAsync(userId, cancellationToken);
        var totals = await expenseQueries.SumByCategoryAsync(userId, range, cancellationToken);
        var allCategories = await categories.ListAsync(userId, includeInactive: true, cancellationToken);

        var allocation = EffectiveBudget.Merge(period, budget?.Allocations, defaults);
        var spending = totals.ToDictionary(total => total.CategoryId, total => total.Total);

        // A category shows when it is active, was allocated to, or actually has spending. A
        // deactivated category with neither disappears, but one with history keeps rendering.
        var lines = allCategories
            .Where(category => category.IsActive
                               || allocation.ContainsKey(category.Id)
                               || spending.ContainsKey(category.Id))
            .Select(category => new BudgetLine(
                category.Id,
                category.Name,
                category.Icon,
                allocation.GetValueOrDefault(category.Id),
                spending.GetValueOrDefault(category.Id)))
            .OrderByDescending(line => line.Spent)
            .ThenBy(line => line.CategoryName, StringComparer.Ordinal)
            .ToList();

        return new MonthlySummary(period, lines);
    }

    public async Task<ExpenseHistoryPage> GetHistoryAsync(
        Guid userId,
        DateRange range,
        ExpensePageCursor? after,
        int pageSize,
        Guid? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        // One extra row tells us whether another page exists without a second COUNT query.
        var rows = await expenseQueries.ListPageAsync(
            userId, range, after, pageSize + 1, categoryId, cancellationToken);

        var hasMore = rows.Count > pageSize;
        var page = (hasMore ? rows.Take(pageSize) : rows).ToList();

        var labels = await LoadCategoryLabelsAsync(userId, page, cancellationToken);
        var items = page.Select(expense => ToItem(expense, labels)).ToList();

        var next = hasMore && page.Count > 0
            ? new ExpensePageCursor(page[^1].ExpenseDate, page[^1].Id)
            : (ExpensePageCursor?)null;

        return new ExpenseHistoryPage(range, items, next);
    }

    public async Task<PeriodStatistics> GetStatisticsAsync(
        Guid userId, MonthPeriod period, DateOnly today, CancellationToken cancellationToken = default)
    {
        var range = DateRange.ForMonth(period);
        var total = await expenseQueries.SumAsync(userId, range, cancellationToken);
        var categoryTotals = await expenseQueries.SumByCategoryAsync(userId, range, cancellationToken);
        var daily = await expenseQueries.SumByDayAsync(userId, range, cancellationToken);
        var largest = await expenseQueries.ListLargestAsync(
            userId, range, LargestExpenseCount, cancellationToken);
        var allCategories = await categories.ListAsync(userId, includeInactive: true, cancellationToken);
        var byId = allCategories.ToDictionary(category => category.Id);

        var categoryShares = categoryTotals
            .Where(category => category.Total > 0)
            .OrderByDescending(category => category.Total)
            .Select(category =>
            {
                var label = byId.GetValueOrDefault(category.CategoryId);
                return new CategoryShare(
                    category.CategoryId,
                    label?.Name ?? string.Empty,
                    label?.Icon ?? string.Empty,
                    category.Total,
                    category.Count,
                    Share(category.Total, total));
            })
            .ToList();

        var expenseCount = categoryTotals.Sum(category => category.Count);
        var daysCounted = CountedDays(period, today);
        var averageDaily = daysCounted > 0
            ? (long)Math.Round(total / (decimal)daysCounted, 0, MidpointRounding.AwayFromZero)
            : 0;

        var previousTotal = await expenseQueries.SumAsync(
            userId, DateRange.ForMonth(period.Previous), cancellationToken);

        var comparison = new PeriodComparison(
            period,
            period.Previous,
            total,
            previousTotal,
            IsIncomplete(period, today),
            IsIncomplete(period.Previous, today));

        var largestExpenses = largest
            .Select(expense =>
            {
                var label = byId.GetValueOrDefault(expense.CategoryId);
                return new LargestExpense(
                    expense.Id,
                    expense.Amount,
                    expense.Description,
                    expense.ExpenseDate,
                    label?.Name ?? string.Empty,
                    label?.Icon ?? string.Empty);
            })
            .ToList();

        return new PeriodStatistics(
            period,
            total,
            expenseCount,
            daysCounted,
            averageDaily,
            categoryShares,
            daily,
            largestExpenses,
            comparison);
    }

    private static decimal Share(long part, long whole) =>
        whole > 0
            ? Math.Round(part * 100m / whole, 1, MidpointRounding.AwayFromZero)
            : 0m;

    /// <summary>
    /// Days elapsed in the period up to <paramref name="today"/>: a month in progress is
    /// averaged over the days that actually happened, not over the whole month.
    /// </summary>
    private static int CountedDays(MonthPeriod period, DateOnly today)
    {
        if (today >= period.LastDay)
        {
            return DateTime.DaysInMonth(period.Year, period.Month);
        }

        return period.Contains(today) ? today.Day : 0;
    }

    private static bool IsIncomplete(MonthPeriod period, DateOnly today) =>
        period.Contains(today) && today < period.LastDay;

    private async Task<Dictionary<Guid, BudgetCategory>> LoadCategoryLabelsAsync(
        Guid userId, IReadOnlyList<Expense> page, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var allCategories = await categories.ListAsync(userId, includeInactive: true, cancellationToken);
        return allCategories.ToDictionary(category => category.Id);
    }

    private static ExpenseListItem ToItem(Expense expense, IReadOnlyDictionary<Guid, BudgetCategory> labels)
    {
        var category = labels.GetValueOrDefault(expense.CategoryId);
        return new ExpenseListItem(
            expense.Id,
            expense.CategoryId,
            category?.Name ?? string.Empty,
            category?.Icon ?? string.Empty,
            expense.Amount,
            expense.Description,
            expense.ExpenseDate,
            expense.CategorizationSource);
    }
}
