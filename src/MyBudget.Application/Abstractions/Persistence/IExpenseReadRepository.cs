using MyBudget.Application.Reporting;
using MyBudget.Domain.Expenses;

namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// The read side of expenses: page listings and aggregations.
/// <para>
/// Kept apart from <see cref="IExpenseRepository"/> because nothing here mutates and every
/// method is already a projection. The sums are computed by PostgreSQL with <c>SUM</c> over the
/// stored rows; a running total is never stored. Every method takes <paramref name="userId"/>
/// first, so a missing scope is a compile error.
/// </para>
/// </summary>
public interface IExpenseReadRepository
{
    /// <summary>
    /// One page of expenses in a range, newest first by <c>(expense_date DESC, id DESC)</c>.
    /// <paramref name="after"/> is the last row of the previous page, or <c>null</c> for the first.
    /// Returns at most <paramref name="take"/> rows.
    /// </summary>
    Task<IReadOnlyList<Expense>> ListPageAsync(
        Guid userId,
        DateRange range,
        ExpensePageCursor? after,
        int take,
        Guid? categoryId = null,
        CancellationToken cancellationToken = default);

    /// <summary>The largest expenses of a range, biggest amount first.</summary>
    Task<IReadOnlyList<Expense>> ListLargestAsync(
        Guid userId,
        DateRange range,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryTotal>> SumByCategoryAsync(
        Guid userId, DateRange range, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyTotal>> SumByDayAsync(
        Guid userId, DateRange range, CancellationToken cancellationToken = default);

    Task<long> SumAsync(
        Guid userId, DateRange range, CancellationToken cancellationToken = default);

    /// <summary>True when the user recorded at least one expense on that local calendar day.</summary>
    Task<bool> ExistsOnAsync(
        Guid userId, DateOnly date, CancellationToken cancellationToken = default);
}
