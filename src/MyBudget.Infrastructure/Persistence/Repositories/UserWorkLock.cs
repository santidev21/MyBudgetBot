using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Telegram;

namespace MyBudget.Infrastructure.Persistence.Repositories;

/// <summary>
/// Serialises one user's work with a PostgreSQL transaction-scoped advisory lock.
/// <para>
/// The lock is keyed by a server-side hash of the user id inside a private namespace, so it
/// cannot collide with the migration lock and different users never wait for each other.
/// Because it is transaction-scoped, a crashed process releases it automatically.
/// </para>
/// </summary>
internal sealed class UserWorkLock(MyBudgetDbContext dbContext) : IUserWorkLock
{
    private const string Namespace = "mybudget:user:";

    public async Task<TResult> ExecuteAsync<TResult>(
        Guid userId,
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var key = Namespace + userId.ToString("N");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({key}))", cancellationToken);

        var result = await work(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
