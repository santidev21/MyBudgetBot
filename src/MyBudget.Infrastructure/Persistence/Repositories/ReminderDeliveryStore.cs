using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;

namespace MyBudget.Infrastructure.Persistence.Repositories;

/// <summary>
/// Per-day scheduled-notification markers in PostgreSQL.
/// <para>
/// The claim is an insert with <c>ON CONFLICT DO NOTHING</c>: the unique key
/// <c>(user_id, kind, local_date)</c> decides, and the number of rows written tells the caller
/// whether it won. Two passes racing for the same user and day can therefore not both send, and
/// neither fails the other.
/// </para>
/// </summary>
internal sealed class ReminderDeliveryStore(MyBudgetDbContext dbContext) : IReminderDeliveryStore
{
    public async Task<bool> TryClaimAsync(
        Guid userId,
        string kind,
        DateOnly localDate,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default)
    {
        var written = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO reminder_deliveries (id, user_id, kind, local_date, sent_at)
             VALUES (gen_random_uuid(), {userId}, {kind}, {localDate}, {claimedAt})
             ON CONFLICT (user_id, kind, local_date) DO NOTHING
             """,
            cancellationToken);

        return written > 0;
    }
}
