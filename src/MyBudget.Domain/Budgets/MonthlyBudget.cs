using MyBudget.Domain.Common;

namespace MyBudget.Domain.Budgets;

/// <summary>
/// The budget of a user for one calendar month.
/// Allocations live in <see cref="Allocations"/>: an allocation row is the historical
/// snapshot, so editing a later month can never rewrite an earlier report.
/// The total is always derived, never stored.
/// </summary>
public sealed class MonthlyBudget : Entity
{
    private readonly List<MonthlyBudgetCategory> _allocations = [];

    private MonthlyBudget()
    {
    }

    public MonthlyBudget(Guid userId, MonthPeriod period)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        UserId = userId;
        Year = period.Year;
        Month = period.Month;
    }

    public Guid UserId { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public MonthPeriod Period => new(Year, Month);

    public IReadOnlyList<MonthlyBudgetCategory> Allocations => _allocations;

    /// <summary>Sum of the category allocations. Derived, never persisted.</summary>
    public long TotalAllocated => _allocations.Sum(a => a.Amount);

    /// <summary>
    /// Creates or updates the allocation of a category for this month only.
    /// Amount is a whole COP value; zero is valid (the category is tracked but not funded).
    /// </summary>
    public MonthlyBudgetCategory SetAllocation(Guid categoryId, long amount)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("Category id is required.", nameof(categoryId));
        }

        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A monthly allocation cannot be negative.");
        }

        var existing = _allocations.FirstOrDefault(a => a.CategoryId == categoryId);
        if (existing is not null)
        {
            existing.ChangeAmount(amount);
            return existing;
        }

        var created = new MonthlyBudgetCategory(UserId, Id, categoryId, amount);
        _allocations.Add(created);
        return created;
    }

    public bool RemoveAllocation(Guid categoryId)
    {
        var existing = _allocations.FirstOrDefault(a => a.CategoryId == categoryId);
        return existing is not null && _allocations.Remove(existing);
    }
}
