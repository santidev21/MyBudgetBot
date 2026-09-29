using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Abstractions.Persistence;

public interface IBudgetRepository
{
    /// <summary>Loads the month with its allocations, or <c>null</c> when the month was never created.</summary>
    Task<MonthlyBudget?> FindByPeriodAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonthlyBudget>> ListByUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Loads every recurring allocation of the user, in any order.</summary>
    Task<IReadOnlyList<BudgetDefault>> ListDefaultsAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Loads one category's recurring allocation, or <c>null</c> when there is none.</summary>
    Task<BudgetDefault?> FindDefaultAsync(
        Guid userId, Guid categoryId, CancellationToken cancellationToken = default);

    void Add(MonthlyBudget budget);

    void AddDefault(BudgetDefault budgetDefault);
}
