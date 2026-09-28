using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Expenses;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL aggregations for the reports.
/// <para>
/// The page listing uses raw SQL on purpose: the keyset tiebreaker is the <c>uuid</c> id, which
/// C# cannot compare with an operator, and PostgreSQL's <c>uuid</c> ordering is exactly the
/// byte order the canonical representation preserves. Doing it in one statement keeps the
/// <c>(user_id, expense_date, id)</c> index in play and keeps the boundary correct.
/// </para>
/// </summary>
internal sealed class ExpenseReadRepository(MyBudgetDbContext dbContext) : IExpenseReadRepository
{
    public async Task<IReadOnlyList<Expense>> ListPageAsync(
        Guid userId,
        DateRange range,
        ExpensePageCursor? after,
        int take,
        CancellationToken cancellationToken = default)
    {
        var hasCursor = after is not null;
        var cursorDate = after?.ExpenseDate ?? DateOnly.MinValue;
        var cursorId = after?.ExpenseId ?? Guid.Empty;

        return await dbContext.Expenses
            .FromSql($"""
                SELECT id, user_id, category_id, amount, description, expense_date,
                       categorization_source, created_at, updated_at
                FROM expenses
                WHERE user_id = {userId}
                  AND expense_date >= {range.From}
                  AND expense_date <= {range.To}
                  AND ({hasCursor} = FALSE
                       OR expense_date < {cursorDate}
                       OR (expense_date = {cursorDate} AND id < {cursorId}))
                ORDER BY expense_date DESC, id DESC
                LIMIT {take}
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Expense>> ListLargestAsync(
        Guid userId,
        DateRange range,
        int take,
        CancellationToken cancellationToken = default)
        => await dbContext.Expenses
            .AsNoTracking()
            .Where(expense => expense.UserId == userId
                              && expense.ExpenseDate >= range.From
                              && expense.ExpenseDate <= range.To)
            .OrderByDescending(expense => expense.Amount)
            .ThenByDescending(expense => expense.ExpenseDate)
            .ThenByDescending(expense => expense.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CategoryTotal>> SumByCategoryAsync(
        Guid userId, DateRange range, CancellationToken cancellationToken = default)
        => await dbContext.Expenses
            .AsNoTracking()
            .Where(expense => expense.UserId == userId
                              && expense.ExpenseDate >= range.From
                              && expense.ExpenseDate <= range.To)
            .GroupBy(expense => expense.CategoryId)
            .Select(group => new CategoryTotal(group.Key, group.Sum(expense => expense.Amount), group.Count()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DailyTotal>> SumByDayAsync(
        Guid userId, DateRange range, CancellationToken cancellationToken = default)
        => await dbContext.Expenses
            .AsNoTracking()
            .Where(expense => expense.UserId == userId
                              && expense.ExpenseDate >= range.From
                              && expense.ExpenseDate <= range.To)
            .GroupBy(expense => expense.ExpenseDate)
            .OrderByDescending(group => group.Key)
            .Select(group => new DailyTotal(group.Key, group.Sum(expense => expense.Amount), group.Count()))
            .ToListAsync(cancellationToken);

    public Task<long> SumAsync(
        Guid userId, DateRange range, CancellationToken cancellationToken = default)
        => dbContext.Expenses
            .AsNoTracking()
            .Where(expense => expense.UserId == userId
                              && expense.ExpenseDate >= range.From
                              && expense.ExpenseDate <= range.To)
            .SumAsync(expense => expense.Amount, cancellationToken);
}
