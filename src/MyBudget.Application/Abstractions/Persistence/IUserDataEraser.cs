namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// Removes every row owned by a user.
/// <para>
/// This is deliberately explicit rather than a database cascade. The composite foreign keys
/// that guarantee user isolation use ON DELETE NO ACTION so that a category with history can
/// never be destroyed by accident, which also means PostgreSQL will not cascade an
/// unrestricted user delete on its own. Erasure therefore has to be an intentional,
/// ordered, transactional operation.
/// </para>
/// </summary>
public interface IUserDataEraser
{
    /// <summary>
    /// Deletes expenses, aliases, allocations, budgets, categories and finally the user,
    /// in dependency order, inside a single transaction. Idempotent: erasing an already
    /// erased user is a no-op.
    /// </summary>
    Task EraseAsync(Guid userId, CancellationToken cancellationToken = default);
}
