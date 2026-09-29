using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Budgets;

/// <summary>
/// One budget threshold a category just crossed, ready for the presentation to render.
/// <see cref="Threshold"/> is the highest threshold newly crossed: a single expense that jumps
/// straight past the limit reports 100, not two separate alerts.
/// </summary>
public sealed record BudgetAlert(
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Allocated,
    long Spent,
    int Threshold,
    decimal UsagePercentage);

/// <summary>
/// Tells the user when a category crosses its monthly allocation.
/// <para>
/// The alert is a policy, so it lives in the application layer and the use cases that write an
/// expense call it and return the result for the presentation to render. Evaluation reads the
/// month's derived usage, records only the thresholds that were not announced yet and reports
/// the highest one. A category with no allocation is ignored: there is no threshold to cross.
/// </para>
/// </summary>
public interface IBudgetAlertService
{
    Task<IReadOnlyList<BudgetAlert>> EvaluateAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class BudgetAlertService(
    IReportService reports,
    IBudgetAlertStore store,
    TimeProvider timeProvider) : IBudgetAlertService
{
    /// <summary>Warn once the category has used this share of its allocation.</summary>
    public const int NearLimitThreshold = 80;

    /// <summary>Warn again, louder, once the allocation is exhausted.</summary>
    public const int OverBudgetThreshold = 100;

    private static readonly int[] Thresholds = [NearLimitThreshold, OverBudgetThreshold];

    public async Task<IReadOnlyList<BudgetAlert>> EvaluateAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var summary = await reports.GetMonthlySummaryAsync(userId, period, cancellationToken);
        var alreadyNotified = (await store.ListAsync(userId, period, cancellationToken))
            .Select(alert => (alert.CategoryId, alert.Threshold))
            .ToHashSet();

        var now = timeProvider.GetUtcNow();
        var alerts = new List<BudgetAlert>();

        foreach (var line in summary.Lines)
        {
            // An unfunded category has no percentage: there is no threshold to cross.
            if (!line.HasBudget || line.UsagePercentage is not { } usage)
            {
                continue;
            }

            var crossed = Thresholds
                .Where(threshold => usage >= threshold && !alreadyNotified.Contains((line.CategoryId, threshold)))
                .ToList();

            if (crossed.Count == 0)
            {
                continue;
            }

            foreach (var threshold in crossed)
            {
                await store.RecordAsync(userId, line.CategoryId, period, threshold, now, cancellationToken);
            }

            alerts.Add(new BudgetAlert(
                line.CategoryId,
                line.CategoryName,
                line.Icon,
                line.Budget,
                line.Spent,
                crossed.Max(),
                usage));
        }

        return alerts;
    }
}
