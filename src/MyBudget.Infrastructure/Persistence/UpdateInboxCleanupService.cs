using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyBudget.Application.Abstractions.Telegram;

namespace MyBudget.Infrastructure.Persistence;

/// <summary>
/// Keeps the update inbox bounded.
/// <para>
/// The inbox exists to make update processing idempotent, not to be a permanent log, so rows
/// outside the retention window are deleted. Without this the table grows for the lifetime of
/// the deployment.
/// </para>
/// </summary>
internal sealed class UpdateInboxCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<UpdateInboxCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                // A hosted service is a singleton, so the scoped store must be resolved from a
                // scope created here. Consuming it directly is a startup error, by design.
                using var scope = scopeFactory.CreateScope();
                var inbox = scope.ServiceProvider.GetRequiredService<IUpdateInbox>();

                var cutoff = timeProvider.GetUtcNow() - Retention;
                var removed = await inbox.PurgeOlderThanAsync(cutoff, stoppingToken);

                if (removed > 0)
                {
                    logger.LogInformation("TelegramInboxPurged {RemovedRows}", removed);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // A failed purge must never take the service down; it is retried on the next tick.
                logger.LogError(exception, "TelegramInboxPurgeFailed");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
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
