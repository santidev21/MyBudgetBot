using MyBudget.Domain.Common;

namespace MyBudget.Domain.Budgets;

/// <summary>
/// A user's recurring budget for one category: the amount that applies to every month from
/// <see cref="EffectiveFrom"/> onwards unless that month overrides it.
/// <para>
/// It is deliberately separate from <see cref="MonthlyBudget"/>: the per-month rows stay the
/// historical snapshot, and the default is the fallback a month reads when it has no row of its
/// own. Changing the default never rewrites a month recorded before it took effect.
/// </para>
/// </summary>
public sealed class BudgetDefault : Entity
{
    private BudgetDefault()
    {
    }

    public BudgetDefault(Guid userId, Guid categoryId, MonthPeriod effectiveFrom, long amount)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("Category id is required.", nameof(categoryId));
        }

        UserId = userId;
        CategoryId = categoryId;
        EffectiveFromYear = effectiveFrom.Year;
        EffectiveFromMonth = effectiveFrom.Month;
        ChangeAmount(amount);
    }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

    public int EffectiveFromYear { get; private set; }

    public int EffectiveFromMonth { get; private set; }

    /// <summary>The first month the default applies to. Later months keep using it unchanged.</summary>
    public MonthPeriod EffectiveFrom => new(EffectiveFromYear, EffectiveFromMonth);

    public long Amount { get; private set; }

    /// <summary>True when this default already covers <paramref name="period"/>.</summary>
    public bool AppliesTo(MonthPeriod period) => !period.IsBefore(EffectiveFrom);

    public void ChangeAmount(long amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A recurring allocation cannot be negative.");
        }

        Amount = amount;
    }
}
