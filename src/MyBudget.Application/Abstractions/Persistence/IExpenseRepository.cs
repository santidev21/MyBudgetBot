using MyBudget.Domain.Budgets;
using MyBudget.Domain.Expenses;

namespace MyBudget.Application.Abstractions.Persistence;

public interface IExpenseRepository
{
    Task<Expense?> FindByIdAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default);

    /// <summary>All expenses of a calendar month, newest first.</summary>
    Task<IReadOnlyList<Expense>> ListByPeriodAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    void Add(Expense expense);

    void Remove(Expense expense);
}
