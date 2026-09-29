using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class BudgetRepository(MyBudgetDbContext dbContext) : IBudgetRepository
{
    public Task<MonthlyBudget?> FindByPeriodAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default)
        => dbContext.MonthlyBudgets
            .Include(budget => budget.Allocations)
            .FirstOrDefaultAsync(
                budget => budget.UserId == userId
                          && budget.Year == period.Year
                          && budget.Month == period.Month,
                cancellationToken);

    public async Task<IReadOnlyList<MonthlyBudget>> ListByUserAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.MonthlyBudgets
            .AsNoTracking()
            .Include(budget => budget.Allocations)
            .Where(budget => budget.UserId == userId)
            .OrderByDescending(budget => budget.Year)
            .ThenByDescending(budget => budget.Month)
            .ToListAsync(cancellationToken);

    public void Add(MonthlyBudget budget) => dbContext.MonthlyBudgets.Add(budget);

    public async Task<IReadOnlyList<BudgetDefault>> ListDefaultsAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.BudgetDefaults
            .AsNoTracking()
            .Where(budgetDefault => budgetDefault.UserId == userId)
            .ToListAsync(cancellationToken);

    public Task<BudgetDefault?> FindDefaultAsync(
        Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
        => dbContext.BudgetDefaults.FirstOrDefaultAsync(
            budgetDefault => budgetDefault.UserId == userId && budgetDefault.CategoryId == categoryId,
            cancellationToken);

    public void AddDefault(BudgetDefault budgetDefault) => dbContext.BudgetDefaults.Add(budgetDefault);
}
