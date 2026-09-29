using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Budgets;

/// <summary>
/// Combines a month's explicit allocations with the user's recurring defaults.
/// <para>
/// The month's own rows win; the default fills the categories the month does not override, but
/// only from the month the default took effect. That is what keeps history intact: a default
/// created in September never appears in August's report.
/// </para>
/// </summary>
internal static class EffectiveBudget
{
    public static Dictionary<Guid, long> Merge(
        MonthPeriod period,
        IEnumerable<MonthlyBudgetCategory>? overrides,
        IEnumerable<BudgetDefault>? defaults)
    {
        var amounts = new Dictionary<Guid, long>();

        foreach (var budgetDefault in defaults ?? [])
        {
            if (budgetDefault.AppliesTo(period))
            {
                amounts[budgetDefault.CategoryId] = budgetDefault.Amount;
            }
        }

        if (overrides is not null)
        {
            foreach (var allocation in overrides)
            {
                amounts[allocation.CategoryId] = allocation.Amount;
            }
        }

        return amounts;
    }
}
