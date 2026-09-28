using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Infrastructure.Persistence.Records;

namespace MyBudget.Infrastructure.Persistence.Repositories;

/// <summary>
/// Pending action drafts in PostgreSQL.
/// <para>
/// Consumption is a single conditional <c>UPDATE</c>: the row is claimed only when it is
/// unconsumed, unexpired and owned by the caller. Exactly one caller can win, which is what
/// makes a replayed callback harmless without a lock of our own.
/// </para>
/// </summary>
internal sealed class PendingActionStore(MyBudgetDbContext dbContext, TimeProvider timeProvider)
    : IPendingActionStore
{
    public async Task CreateAsync(
        PendingAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        dbContext.PendingActions.Add(new PendingActionRecord
        {
            Id = action.Id,
            UserId = action.UserId,
            Action = action.Action,
            Payload = action.Payload,
            ExpiresAt = action.ExpiresAt,
            CreatedAt = timeProvider.GetUtcNow(),
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PendingAction?> ConsumeAsync(
        Guid userId, Guid actionId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        // Ownership, expiry and the consume-once rule all live in the WHERE clause, so the
        // database decides the winner in one statement. ExecuteUpdate bypasses the change
        // tracker, hence the untracked read below.
        var claimed = await dbContext.PendingActions
            .Where(action => action.Id == actionId
                             && action.UserId == userId
                             && action.ConsumedAt == null
                             && action.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(action => action.ConsumedAt, now),
                cancellationToken);

        if (claimed == 0)
        {
            return null;
        }

        var record = await dbContext.PendingActions
            .AsNoTracking()
            .FirstOrDefaultAsync(action => action.Id == actionId, cancellationToken);

        return record is null
            ? null
            : new PendingAction(record.Id, record.UserId, record.Action, record.Payload, record.ExpiresAt);
    }
}
