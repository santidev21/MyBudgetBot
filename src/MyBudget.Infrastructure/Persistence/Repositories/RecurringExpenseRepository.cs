using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Recurring;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class RecurringExpenseRepository(MyBudgetDbContext dbContext) : IRecurringExpenseRepository
{
    public Task<RecurringExpense?> FindByIdAsync(
        Guid userId, Guid recurringExpenseId, CancellationToken cancellationToken = default)
        => dbContext.RecurringExpenses.FirstOrDefaultAsync(
            rule => rule.UserId == userId && rule.Id == recurringExpenseId, cancellationToken);

    public async Task<IReadOnlyList<RecurringExpense>> ListAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.RecurringExpenses
            .AsNoTracking()
            .Where(rule => rule.UserId == userId)
            .OrderByDescending(rule => rule.IsActive)
            .ThenBy(rule => rule.DayOfMonth)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RecurringExpense>> ListActiveAsync(
        CancellationToken cancellationToken = default)
        // Tracked on purpose: the pass records how far each rule got before saving.
        => await dbContext.RecurringExpenses
            .Where(rule => rule.IsActive)
            .ToListAsync(cancellationToken);

    public void Add(RecurringExpense rule) => dbContext.RecurringExpenses.Add(rule);

    public void Remove(RecurringExpense rule) => dbContext.RecurringExpenses.Remove(rule);
}
