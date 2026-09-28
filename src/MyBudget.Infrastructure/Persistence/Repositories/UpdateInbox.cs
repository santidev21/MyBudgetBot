using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Infrastructure.Persistence.Records;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class UpdateInbox(
    MyBudgetDbContext dbContext,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IUpdateInbox
{
    private const int MaxErrorLength = 500;

    public async Task<bool> TryClaimAsync(long updateId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.TelegramUpdates
            .FirstOrDefaultAsync(record => record.UpdateId == updateId, cancellationToken);

        if (existing is not null)
        {
            // Processed and ignored updates are both settled. Only a failure reopens a claim:
            // an update skipped on purpose (stale, duplicate transport, not allowlisted)
            // must never be acted on later just because Telegram delivered it again.
            if (existing.Status is TelegramUpdateStatus.Processed or TelegramUpdateStatus.Ignored)
            {
                return false;
            }

            // A previous delivery failed or was interrupted mid-flight: allow one more attempt.
            existing.Status = TelegramUpdateStatus.Received;
            existing.Attempts++;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }

        var record = new TelegramUpdateRecord
        {
            UpdateId = updateId,
            ReceivedAt = timeProvider.GetUtcNow(),
            Status = TelegramUpdateStatus.Received,
            Attempts = 1,
        };

        dbContext.TelegramUpdates.Add(record);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (UniqueConstraintViolationException)
        {
            // A concurrent delivery of the same update won the race; it owns the processing.
            unitOfWork.Detach(record);
            return false;
        }
    }

    public Task CompleteAsync(long updateId, Guid? userId, CancellationToken cancellationToken = default) =>
        MutateAsync(
            updateId,
            record =>
            {
                record.Status = TelegramUpdateStatus.Processed;
                record.ProcessedAt = timeProvider.GetUtcNow();
                record.LastError = null;
                record.UserId = userId ?? record.UserId;
            },
            cancellationToken);

    public Task FailAsync(long updateId, string reason, CancellationToken cancellationToken = default) =>
        MutateAsync(
            updateId,
            record =>
            {
                record.Status = TelegramUpdateStatus.Failed;
                record.LastError = Truncate(reason);
            },
            cancellationToken);

    public Task IgnoreAsync(
        long updateId, Guid? userId, string reason, CancellationToken cancellationToken = default) =>
        MutateAsync(
            updateId,
            record =>
            {
                record.Status = TelegramUpdateStatus.Ignored;
                record.ProcessedAt = timeProvider.GetUtcNow();
                record.LastError = Truncate(reason);
                record.UserId = userId ?? record.UserId;
            },
            cancellationToken);

    public async Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken = default) =>
        // Bulk delete that deliberately bypasses the change tracker: this runs from its own
        // scope, and loading rows only to delete them would be wasteful.
        await dbContext.TelegramUpdates
            .Where(record => record.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

    private async Task MutateAsync(
        long updateId, Action<TelegramUpdateRecord> mutate, CancellationToken cancellationToken)
    {
        var record = await dbContext.TelegramUpdates
            .FirstOrDefaultAsync(candidate => candidate.UpdateId == updateId, cancellationToken);

        if (record is null)
        {
            return;
        }

        mutate(record);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string Truncate(string value) =>
        value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
}
