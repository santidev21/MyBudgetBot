using MyBudget.Application.Expenses;
using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Reporting;

/// <summary>
/// A month's spending against that month's historical allocation.
/// <para>
/// The lines are the same <see cref="BudgetLine"/> the budget flow uses, and the totals are
/// derived from them: <c>SUM</c> over stored rows, never a stored total. A category with no
/// allocation still appears when it has spending, so the month never hides activity.
/// </para>
/// </summary>
public sealed record MonthlySummary(MonthPeriod Period, IReadOnlyList<BudgetLine> Lines)
{
    public long TotalAllocated => BudgetMath.TotalBudget(Lines);

    public long TotalSpent => BudgetMath.TotalSpent(Lines);

    public decimal? UsagePercentage => BudgetMath.OverallUsagePercentage(Lines);
}

/// <summary>One page of the date-range history, plus the cursor for the next page.</summary>
public sealed record ExpenseHistoryPage(
    DateRange Range,
    IReadOnlyList<ExpenseListItem> Items,
    ExpensePageCursor? NextCursor)
{
    public bool HasMore => NextCursor is not null;
}

/// <summary>One category's share of a period's spending.</summary>
public sealed record CategoryShare(
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Spent,
    int Count,
    decimal SharePercentage);

/// <summary>One of the period's largest expenses, with its category label.</summary>
public sealed record LargestExpense(
    Guid ExpenseId,
    long Amount,
    string? Description,
    DateOnly ExpenseDate,
    string CategoryName,
    string Icon);

/// <summary>
/// A period's total next to the immediately preceding period's.
/// <para>
/// <see cref="PeriodIncomplete"/> is how the caller knows to warn: a month still in progress
/// cannot be compared like a full one. The previous period can be incomplete too when the
/// current period is in the future.
/// </para>
/// </summary>
public sealed record PeriodComparison(
    MonthPeriod Period,
    MonthPeriod Previous,
    long Total,
    long PreviousTotal,
    bool PeriodIncomplete,
    bool PreviousPeriodIncomplete)
{
    public long Difference => Total - PreviousTotal;

    /// <summary>
    /// Percentage change, rounded to one decimal. <c>null</c> when the previous period had no
    /// spending, because a change from zero is not a percentage.
    /// </summary>
    public decimal? ChangePercentage =>
        PreviousTotal > 0
            ? Math.Round(Difference * 100m / PreviousTotal, 1, MidpointRounding.AwayFromZero)
            : null;
}

/// <summary>Everything the statistics screen renders for one month.</summary>
public sealed record PeriodStatistics(
    MonthPeriod Period,
    long Total,
    int ExpenseCount,
    int DaysCounted,
    long AverageDaily,
    IReadOnlyList<CategoryShare> Categories,
    IReadOnlyList<DailyTotal> Daily,
    IReadOnlyList<LargestExpense> Largest,
    PeriodComparison Comparison);
