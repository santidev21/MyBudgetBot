namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// Transactional boundary over the persistence context.
/// Application services never see the <c>DbContext</c> itself.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists pending changes.
    /// <para>
    /// Throws <see cref="UniqueConstraintViolationException"/> when the database rejects a
    /// write because of a unique constraint, so callers can react without knowing Npgsql.
    /// </para>
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops tracking an entity whose write was rejected. A failed insert otherwise stays in
    /// the change tracker and every later save in the same scope would fail again.
    /// </summary>
    void Detach(object entity);
}
