using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Abstractions.Persistence;

public interface IBudgetRepository
{
    /// <summary>Loads the month with its allocations, or <c>null</c> when the month was never created.</summary>
    Task<MonthlyBudget?> FindByPeriodAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonthlyBudget>> ListByUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    void Add(MonthlyBudget budget);
}
