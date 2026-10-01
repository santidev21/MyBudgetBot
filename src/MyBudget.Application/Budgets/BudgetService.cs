using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Budgets;

/// <summary>One category line of a month's budget, with the category as it is today.</summary>
public sealed record MonthlyBudgetLine(
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Amount,
    bool IsActive,
    bool IsRecurring = false);

/// <summary>
/// A month's budget: each category's effective allocation — the month's own row if it has one,
/// otherwise the recurring default — with the total derived. A month that was never created and
/// has no default lists its categories at zero.
/// </summary>
public sealed record MonthlyBudgetView(MonthPeriod Period, IReadOnlyList<MonthlyBudgetLine> Lines)
{
    public long TotalAllocated => Lines.Sum(line => line.Amount);
}

/// <summary>Whether an assignment applies to one month or becomes the recurring default.</summary>
public enum BudgetScope
{
    /// <summary>An override for the given month only; other months keep the default.</summary>
    Month,

    /// <summary>The recurring amount every month from now on, including the given one.</summary>
    AllMonths,
}

public enum BudgetWriteStatus
{
    Saved,

    /// <summary>The period is before the current month; past months are immutable in the product.</summary>
    PastMonth,

    CategoryNotFound,

    /// <summary>The category exists but is deactivated, so it cannot receive new allocations.</summary>
    CategoryInactive,
}

public sealed record BudgetWriteResult(BudgetWriteStatus Status, MonthlyBudget? Budget = null)
{
    public bool Saved => Status == BudgetWriteStatus.Saved;

    public static BudgetWriteResult Ok(MonthlyBudget? budget) => new(BudgetWriteStatus.Saved, budget);

    public static BudgetWriteResult PastMonth() => new(BudgetWriteStatus.PastMonth);

    public static BudgetWriteResult CategoryNotFound() => new(BudgetWriteStatus.CategoryNotFound);

    public static BudgetWriteResult CategoryInactive() => new(BudgetWriteStatus.CategoryInactive);
}

/// <summary>
/// The outcome of turning a month's explicit allocations into recurring defaults.
/// <see cref="PromotedCount"/> is how many categories were made to recur: zero when the month
/// had no overrides, or when every one of them was already covered by a default that starts
/// after the month.
/// </summary>
public sealed record BudgetPromotionResult(BudgetWriteStatus Status, int PromotedCount)
{
    public bool Saved => Status == BudgetWriteStatus.Saved;
}

/// <summary>
/// Monthly budget management, always for the current or a future month.
/// <para>
/// The budget of a past month is immutable in the product. The database does not enforce that
/// (repairing data must stay possible), so the rule lives here and is reported as a reason
/// rather than silently refused. "Today" is passed in by the caller so the rule is testable and
/// resolved in the user's own time zone.
/// </para>
/// </summary>
public interface IBudgetService
{
    /// <summary>
    /// The effective allocations for a month, together with every active category. A category
    /// that was deactivated still appears when it holds an allocation for that month, because
    /// old months must keep rendering as they did.
    /// </summary>
    Task<MonthlyBudgetView> GetMonthAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    /// <summary>True when the user already has at least one recurring allocation.</summary>
    Task<bool> HasDefaultsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns an amount to a category. <see cref="BudgetScope.Month"/> writes only that month's
    /// override; <see cref="BudgetScope.AllMonths"/> makes it the recurring default and clears
    /// the month's own override so the new default shows there too.
    /// </summary>
    Task<BudgetWriteResult> SetAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, long amount, BudgetScope scope,
        DateOnly today, CancellationToken cancellationToken = default);

    Task<BudgetWriteResult> RemoveAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes every explicit allocation of the month into a recurring default, so later months
    /// inherit the budget without an override of their own. A default that already exists keeps
    /// the month it started; the month's own rows are cleared so it reads the default like any
    /// other month. A past month is refused with a reason.
    /// </summary>
    Task<BudgetPromotionResult> PromoteMonthToDefaultsAsync(
        Guid userId, MonthPeriod period, DateOnly today, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BudgetService(
    IBudgetRepository budgets,
    ICategoryRepository categories,
    IUnitOfWork unitOfWork) : IBudgetService
{
    public async Task<MonthlyBudgetView> GetMonthAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var budget = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
        var defaults = await budgets.ListDefaultsAsync(userId, cancellationToken) ?? [];
        var allCategories = await categories.ListAsync(userId, includeInactive: true, cancellationToken);

        var amounts = EffectiveBudget.Merge(period, budget?.Allocations, defaults);
        var overridden = budget?.Allocations
            .Select(allocation => allocation.CategoryId)
            .ToHashSet() ?? [];

        // Active categories always show, so the user can see what is still unfunded. An inactive
        // category only shows when this month actually allocated to it.
        var lines = allCategories
            .Where(category => category.IsActive || amounts.ContainsKey(category.Id))
            .Select(category => new MonthlyBudgetLine(
                category.Id,
                category.Name,
                category.Icon,
                amounts.GetValueOrDefault(category.Id),
                category.IsActive,
                amounts.ContainsKey(category.Id) && !overridden.Contains(category.Id)))
            .ToList();

        return new MonthlyBudgetView(period, lines);
    }

    public async Task<bool> HasDefaultsAsync(
        Guid userId, CancellationToken cancellationToken = default) =>
        (await budgets.ListDefaultsAsync(userId, cancellationToken) ?? []).Count > 0;

    public async Task<BudgetWriteResult> SetAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, long amount, BudgetScope scope,
        DateOnly today, CancellationToken cancellationToken = default)
    {
        if (IsPast(period, today))
        {
            return BudgetWriteResult.PastMonth();
        }

        var category = await categories.FindByIdAsync(userId, categoryId, cancellationToken);
        if (category is null)
        {
            return BudgetWriteResult.CategoryNotFound();
        }

        if (!category.IsActive)
        {
            return BudgetWriteResult.CategoryInactive();
        }

        if (scope == BudgetScope.AllMonths)
        {
            var existingDefault = await budgets.FindDefaultAsync(userId, categoryId, cancellationToken);
            if (existingDefault is null)
            {
                budgets.AddDefault(new BudgetDefault(userId, categoryId, period, amount));
            }
            else
            {
                // Editing keeps the month the default took effect: it must not start applying
                // to months it never covered.
                existingDefault.ChangeAmount(amount);
            }

            // "Every month" includes this one: drop its override so the new default shows.
            var month = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
            month?.RemoveAllocation(categoryId);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return BudgetWriteResult.Ok(month);
        }

        var budget = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
        if (budget is null)
        {
            budget = new MonthlyBudget(userId, period);
            budgets.Add(budget);
        }

        budget.SetAllocation(categoryId, amount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BudgetWriteResult.Ok(budget);
    }

    public async Task<BudgetWriteResult> RemoveAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, DateOnly today,
        CancellationToken cancellationToken = default)
    {
        if (IsPast(period, today))
        {
            return BudgetWriteResult.PastMonth();
        }

        var budget = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
        if (budget is null)
        {
            return BudgetWriteResult.Ok(null);
        }

        budget.RemoveAllocation(categoryId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BudgetWriteResult.Ok(budget);
    }

    public async Task<BudgetPromotionResult> PromoteMonthToDefaultsAsync(
        Guid userId, MonthPeriod period, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (IsPast(period, today))
        {
            return new BudgetPromotionResult(BudgetWriteStatus.PastMonth, 0);
        }

        var budget = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
        if (budget is null || budget.Allocations.Count == 0)
        {
            return new BudgetPromotionResult(BudgetWriteStatus.Saved, 0);
        }

        var promoted = 0;
        foreach (var allocation in budget.Allocations.ToList())
        {
            var existing = await budgets.FindDefaultAsync(userId, allocation.CategoryId, cancellationToken);

            if (existing is null)
            {
                budgets.AddDefault(new BudgetDefault(userId, allocation.CategoryId, period, allocation.Amount));
            }
            else if (!period.IsBefore(existing.EffectiveFrom))
            {
                // Editing keeps the month the default took effect: it must not start applying to
                // months it never covered.
                existing.ChangeAmount(allocation.Amount);
            }
            else
            {
                // The default starts after this month, so it does not cover it. The override has
                // to stay, or the month would lose the allocation it is showing.
                continue;
            }

            // The month now reads the default, exactly like every month after it.
            budget.RemoveAllocation(allocation.CategoryId);
            promoted++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new BudgetPromotionResult(BudgetWriteStatus.Saved, promoted);
    }

    private static bool IsPast(MonthPeriod period, DateOnly today) =>
        period.IsBefore(MonthPeriod.FromDate(today));
}
