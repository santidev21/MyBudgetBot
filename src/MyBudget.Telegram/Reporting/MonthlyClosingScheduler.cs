using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyBudget.Application.Reporting;

namespace MyBudget.Telegram.Reporting;

/// <summary>
/// Periodically prepares and sends the closing of the month that just ended.
/// <para>
/// The pass is exactly-once: the application service claims a marker per user and closed month
/// before returning anything, so running more often than needed, or restarting, cannot send the
/// same closing twice. It runs only when Telegram is configured, because a report the user is
/// never told about would be worse than waiting for the next start.
/// </para>
/// </summary>
internal sealed class MonthlyClosingScheduler(
    IServiceScopeFactory scopeFactory,
    MonthlyClosingNotifier notifier,
    ILogger<MonthlyClosingScheduler> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Lets the host finish starting before the first pass touches the database.</summary>
    internal static readonly TimeSpan StartDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);

        do
        {
            await RunOnceAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // A hosted service is a singleton and the use case is scoped, so every pass gets
            // its own scope, exactly like the recurring scheduler.
            using var scope = scopeFactory.CreateScope();
            var closings = scope.ServiceProvider.GetRequiredService<IMonthlyClosingService>();

            var due = await closings.PrepareDueAsync(cancellationToken);
            if (due.Count == 0)
            {
                return;
            }

            logger.LogInformation("MonthlyClosingsPrepared {Users}", due.Count);
            await notifier.NotifyAsync(due, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down; anything not yet claimed is retried on the next start.
        }
        catch (Exception exception)
        {
            // A failed pass must never take the service down; it is retried on the next tick.
            logger.LogError(exception, "MonthlyClosingPreparationFailed");
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
