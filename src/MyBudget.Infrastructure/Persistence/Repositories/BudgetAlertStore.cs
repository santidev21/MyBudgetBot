using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Budgets;

namespace MyBudget.Infrastructure.Persistence.Repositories;

/// <summary>
/// Budget alert markers in PostgreSQL.
/// <para>
/// The write is an upsert with <c>ON CONFLICT DO NOTHING</c> because EF Core has no equivalent:
/// the unique key decides, so the expense path and the recurring pass can race without either
/// failing the other's update.
/// </para>
/// </summary>
internal sealed class BudgetAlertStore(MyBudgetDbContext dbContext) : IBudgetAlertStore
{
    public async Task<IReadOnlyList<NotifiedBudgetAlert>> ListAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.BudgetAlerts
            .AsNoTracking()
            .Where(alert => alert.UserId == userId
                            && alert.Year == period.Year
                            && alert.Month == period.Month)
            .Select(alert => new { alert.CategoryId, alert.Threshold })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new NotifiedBudgetAlert(row.CategoryId, row.Threshold)).ToList();
    }

    public async Task RecordAsync(
        Guid userId,
        Guid categoryId,
        MonthPeriod period,
        int threshold,
        DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default)
        => await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO budget_alerts (id, user_id, category_id, year, month, threshold, notified_at)
             VALUES (gen_random_uuid(), {userId}, {categoryId}, {(short)period.Year},
                     {(short)period.Month}, {(short)threshold}, {notifiedAt})
             ON CONFLICT (user_id, category_id, year, month, threshold) DO NOTHING
             """,
            cancellationToken);
}
