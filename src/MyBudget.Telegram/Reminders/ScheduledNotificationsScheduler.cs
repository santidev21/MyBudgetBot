using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyBudget.Application.Reminders;
using MyBudget.Application.Reporting;
using MyBudget.Telegram.Reporting;

namespace MyBudget.Telegram.Reminders;

/// <summary>
/// Periodically runs the per-user scheduled notifications: the evening reminder and the closing
/// of a month.
/// <para>
/// It ticks every minute because both triggers are the user's own local time. Each use case
/// claims its marker before returning anything, so running often cannot send the same message
/// twice. It runs only when Telegram is configured, because a message the user is never told
/// about would be worse than waiting.
/// </para>
/// </summary>
internal sealed class ScheduledNotificationsScheduler(
    IServiceScopeFactory scopeFactory,
    DailyReminderNotifier reminderNotifier,
    MonthlyClosingNotifier closingNotifier,
    ILogger<ScheduledNotificationsScheduler> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

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
            // A hosted service is a singleton and the use cases are scoped, so every pass gets its
            // own scope, exactly like the recurring scheduler.
            using var scope = scopeFactory.CreateScope();

            var reminders = scope.ServiceProvider.GetRequiredService<IDailyReminderService>();
            var reminderDue = await reminders.PrepareDueAsync(cancellationToken);
            if (reminderDue.Count > 0)
            {
                logger.LogInformation("DailyRemindersPrepared {Users}", reminderDue.Count);
                await reminderNotifier.NotifyAsync(reminderDue, cancellationToken);
            }

            var closings = scope.ServiceProvider.GetRequiredService<IMonthlyClosingService>();
            var closingDue = await closings.PrepareDueAsync(cancellationToken);
            if (closingDue.Count > 0)
            {
                logger.LogInformation("MonthlyClosingsPrepared {Users}", closingDue.Count);
                await closingNotifier.NotifyAsync(closingDue, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down; anything not yet claimed is retried on the next start.
        }
        catch (Exception exception)
        {
            // A failed pass must never take the service down; it is retried on the next tick.
            logger.LogError(exception, "ScheduledNotificationPreparationFailed");
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
