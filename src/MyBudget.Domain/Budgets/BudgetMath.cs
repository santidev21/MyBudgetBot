namespace MyBudget.Domain.Budgets;

/// <summary>
/// The budget position of a single category for a period: what was allocated and what was spent.
/// Pure data, derived from a historical allocation plus expenses of the same month.
/// </summary>
public readonly record struct BudgetLine(
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Budget,
    long Spent)
{
    public bool HasBudget => Budget > 0;

    public long Remaining => Budget - Spent;

    public bool IsOverspent => Spent > Budget;

    /// <summary>Amount spent above the allocation. Zero when within budget (see spec: never blocked).</summary>
    public long Overage => IsOverspent ? Spent - Budget : 0;

    /// <summary>
    /// Percentage of the allocation used, rounded to one decimal.
    /// <c>null</c> when there is no allocation, because dividing by zero is meaningless.
    /// </summary>
    public decimal? UsagePercentage => BudgetMath.UsagePercentage(Budget, Spent);
}

/// <summary>
/// Pure budget arithmetic. No I/O, no clock, no culture: trivially unit testable.
/// </summary>
public static class BudgetMath
{
    /// <summary>
    /// Usage as a percentage rounded to one decimal place.
    /// Returns <c>null</c> when <paramref name="budget"/> is zero or negative: an unfunded
    /// category has no meaningful usage percentage.
    /// </summary>
    public static decimal? UsagePercentage(long budget, long spent)
    {
        if (budget <= 0)
        {
            return null;
        }

        return Math.Round(spent * 100m / budget, 1, MidpointRounding.AwayFromZero);
    }

    public static long TotalBudget(IEnumerable<BudgetLine> lines) => lines.Sum(line => line.Budget);

    public static long TotalSpent(IEnumerable<BudgetLine> lines) => lines.Sum(line => line.Spent);

    /// <summary>Positive when money is left, negative when the month is overspent overall.</summary>
    public static long TotalRemaining(IEnumerable<BudgetLine> lines) =>
        TotalBudget(lines) - TotalSpent(lines);

    public static decimal? OverallUsagePercentage(IEnumerable<BudgetLine> lines)
    {
        var materialized = lines as IReadOnlyCollection<BudgetLine> ?? lines.ToList();
        return UsagePercentage(TotalBudget(materialized), TotalSpent(materialized));
    }
}
