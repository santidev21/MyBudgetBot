using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Budgets;

namespace MyBudget.Infrastructure.Persistence.Repositories;

/// <summary>
/// Monthly closing markers in PostgreSQL.
/// <para>
/// The claim is an insert with <c>ON CONFLICT DO NOTHING</c>: the unique key decides, and the
/// number of rows written tells the caller whether it won. Two passes racing for the same user
/// and month can therefore not both send, and neither fails the other.
/// </para>
/// </summary>
internal sealed class MonthlyClosingStore(MyBudgetDbContext dbContext) : IMonthlyClosingStore
{
    public async Task<bool> TryClaimAsync(
        Guid userId,
        MonthPeriod period,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default)
    {
        var written = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO monthly_closings (id, user_id, year, month, sent_at)
             VALUES (gen_random_uuid(), {userId}, {(short)period.Year}, {(short)period.Month}, {claimedAt})
             ON CONFLICT (user_id, year, month) DO NOTHING
             """,
            cancellationToken);

        return written > 0;
    }
}
