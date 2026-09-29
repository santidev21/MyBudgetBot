using MyBudget.Domain.Recurring;

namespace MyBudget.Application.Abstractions.Persistence;

public interface IRecurringExpenseRepository
{
    Task<RecurringExpense?> FindByIdAsync(
        Guid userId, Guid recurringExpenseId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecurringExpense>> ListAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active rule across all users, for the scheduler.
    /// <para>
    /// Deliberately not user-scoped: the nightly job has to discover whose rules are due, and
    /// there is no single <c>userId</c> to scope by. Every rule it returns is still only ever
    /// written through its own <see cref="RecurringExpense.UserId"/>.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<RecurringExpense>> ListActiveAsync(CancellationToken cancellationToken = default);

    void Add(RecurringExpense rule);

    void Remove(RecurringExpense rule);
}
