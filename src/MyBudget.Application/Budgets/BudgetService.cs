using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Budgets;

/// <summary>One category line of a month's budget, with the category as it is today.</summary>
public sealed record MonthlyBudgetLine(
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Amount,
    bool IsActive);

/// <summary>
/// A month's budget: the allocations historically recorded for that month, with the total
/// derived. A month that was never created simply lists its categories at zero.
/// </summary>
public sealed record MonthlyBudgetView(MonthPeriod Period, IReadOnlyList<MonthlyBudgetLine> Lines)
{
    public long TotalAllocated => Lines.Sum(line => line.Amount);
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

public enum BudgetCopyStatus
{
    Copied,

    /// <summary>The month to copy is before the current month; past months are immutable.</summary>
    PastMonth,

    /// <summary>The previous month has no budget at all, so there is nothing to copy.</summary>
    NoPreviousBudget,
}

public sealed record BudgetCopyResult(BudgetCopyStatus Status, MonthlyBudget? Budget = null);

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
    /// The allocations recorded for a month, together with every active category. A category
    /// that was deactivated still appears when it holds an allocation for that month, because
    /// old months must keep rendering as they did.
    /// </summary>
    Task<MonthlyBudgetView> GetMonthAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    Task<BudgetWriteResult> SetAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, long amount, DateOnly today,
        CancellationToken cancellationToken = default);

    Task<BudgetWriteResult> RemoveAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies every allocation of the previous month into this one. Explicitly requested by the
    /// user; nothing is ever copied automatically.
    /// </summary>
    Task<BudgetCopyResult> CopyPreviousMonthAsync(
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
        var allCategories = await categories.ListAsync(userId, includeInactive: true, cancellationToken);

        var amounts = new Dictionary<Guid, long>();
        if (budget is not null)
        {
            foreach (var allocation in budget.Allocations)
            {
                amounts[allocation.CategoryId] = allocation.Amount;
            }
        }

        // Active categories always show, so the user can see what is still unfunded. An inactive
        // category only shows when this month actually allocated to it.
        var lines = allCategories
            .Where(category => category.IsActive || amounts.ContainsKey(category.Id))
            .Select(category => new MonthlyBudgetLine(
                category.Id,
                category.Name,
                category.Icon,
                amounts.GetValueOrDefault(category.Id),
                category.IsActive))
            .ToList();

        return new MonthlyBudgetView(period, lines);
    }

    public Task<BudgetWriteResult> SetAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, long amount, DateOnly today,
        CancellationToken cancellationToken = default)
        => WriteAsync(
            userId, period, categoryId, today,
            budget => budget.SetAllocation(categoryId, amount),
            cancellationToken);

    public Task<BudgetWriteResult> RemoveAllocationAsync(
        Guid userId, MonthPeriod period, Guid categoryId, DateOnly today,
        CancellationToken cancellationToken = default)
        => WriteAsync(
            userId, period, categoryId, today,
            budget => budget.RemoveAllocation(categoryId),
            cancellationToken);

    public async Task<BudgetCopyResult> CopyPreviousMonthAsync(
        Guid userId, MonthPeriod period, DateOnly today, CancellationToken cancellationToken = default)
    {
        if (IsPast(period, today))
        {
            return new BudgetCopyResult(BudgetCopyStatus.PastMonth);
        }

        var previous = await budgets.FindByPeriodAsync(userId, period.Previous, cancellationToken);
        if (previous is null || previous.Allocations.Count == 0)
        {
            return new BudgetCopyResult(BudgetCopyStatus.NoPreviousBudget);
        }

        var target = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
        if (target is null)
        {
            target = new MonthlyBudget(userId, period);
            budgets.Add(target);
        }

        target.CopyAllocationsFrom(previous);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new BudgetCopyResult(BudgetCopyStatus.Copied, target);
    }

    private async Task<BudgetWriteResult> WriteAsync(
        Guid userId,
        MonthPeriod period,
        Guid categoryId,
        DateOnly today,
        Action<MonthlyBudget> apply,
        CancellationToken cancellationToken)
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

        var budget = await budgets.FindByPeriodAsync(userId, period, cancellationToken);
        if (budget is null)
        {
            budget = new MonthlyBudget(userId, period);
            budgets.Add(budget);
        }

        apply(budget);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return BudgetWriteResult.Ok(budget);
    }

    private static bool IsPast(MonthPeriod period, DateOnly today) =>
        period.IsBefore(MonthPeriod.FromDate(today));
}
