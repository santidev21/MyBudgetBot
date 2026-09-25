namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// Transactional boundary over the persistence context.
/// Application services never see the <c>DbContext</c> itself.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
