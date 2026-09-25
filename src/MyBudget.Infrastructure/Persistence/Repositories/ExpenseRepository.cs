using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Expenses;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class ExpenseRepository(MyBudgetDbContext dbContext) : IExpenseRepository
{
    public Task<Expense?> FindByIdAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default)
        => dbContext.Expenses.FirstOrDefaultAsync(
            expense => expense.UserId == userId && expense.Id == expenseId, cancellationToken);

    public async Task<IReadOnlyList<Expense>> ListByPeriodAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default)
        => await dbContext.Expenses
            .AsNoTracking()
            .Where(expense => expense.UserId == userId
                              && expense.ExpenseDate >= period.FirstDay
                              && expense.ExpenseDate <= period.LastDay)
            .OrderByDescending(expense => expense.ExpenseDate)
            .ThenByDescending(expense => expense.Id)
            .ToListAsync(cancellationToken);

    public void Add(Expense expense) => dbContext.Expenses.Add(expense);

    public void Remove(Expense expense) => dbContext.Expenses.Remove(expense);
}
