using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>A budget threshold already notified for one category in one month.</summary>
public readonly record struct NotifiedBudgetAlert(Guid CategoryId, int Threshold);

/// <summary>
/// Remembers which budget thresholds were already announced, so crossing 80 % or 100 % tells
/// the user once and never again for the same category and month.
/// <para>
/// Operational state, not a domain entity: it says nothing about money, it only prevents a
/// repeated notification. The record is written with an upsert so a race between the expense
/// path and the recurring pass cannot fail the write.
/// </para>
/// </summary>
public interface IBudgetAlertStore
{
    Task<IReadOnlyList<NotifiedBudgetAlert>> ListAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    /// <summary>Records a threshold as notified, ignoring a concurrent duplicate.</summary>
    Task RecordAsync(
        Guid userId,
        Guid categoryId,
        MonthPeriod period,
        int threshold,
        DateTimeOffset notifiedAt,
        CancellationToken cancellationToken = default);
}
