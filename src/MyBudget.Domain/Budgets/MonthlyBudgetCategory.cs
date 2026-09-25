using MyBudget.Domain.Common;

namespace MyBudget.Domain.Budgets;

/// <summary>
/// One category allocation inside a <see cref="MonthlyBudget"/>.
/// Rows are append-only in practice: a past month is never rewritten automatically.
/// </summary>
public sealed class MonthlyBudgetCategory : Entity
{
    private MonthlyBudgetCategory()
        : base(keyGeneratedByStore: true)
    {
    }

    internal MonthlyBudgetCategory(Guid userId, Guid monthlyBudgetId, Guid categoryId, long amount)
        : base(keyGeneratedByStore: true)
    {
        UserId = userId;
        MonthlyBudgetId = monthlyBudgetId;
        CategoryId = categoryId;
        ChangeAmount(amount);
    }

    public Guid UserId { get; private set; }

    public Guid MonthlyBudgetId { get; private set; }

    public Guid CategoryId { get; private set; }

    public long Amount { get; private set; }

    internal void ChangeAmount(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A monthly allocation cannot be negative.");
        }

        Amount = amount;
    }
}
