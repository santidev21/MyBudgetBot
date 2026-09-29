using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyBudget.Application.Recurring;

namespace MyBudget.Telegram.Recurring;

/// <summary>
/// Periodically turns every due recurring rule into an expense.
/// <para>
/// The pass is idempotent: each rule remembers the last occurrence it generated, and that
/// update commits in the same transaction as the expense, so running more often than needed
/// cannot duplicate a month. It runs only when Telegram is configured, because recording an
/// expense the user is never told about would be worse than waiting.
/// </para>
/// </summary>
internal sealed class RecurringExpenseScheduler(
    IServiceScopeFactory scopeFactory,
    RecurringExpenseNotifier notifier,
    ILogger<RecurringExpenseScheduler> logger) : BackgroundService
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
            // its own scope, exactly like the polling service resolves the dispatcher.
            using var scope = scopeFactory.CreateScope();
            var recurring = scope.ServiceProvider.GetRequiredService<IRecurringExpenseService>();

            var results = await recurring.ApplyDueAsync(cancellationToken);
            if (results.Count == 0)
            {
                return;
            }

            logger.LogInformation(
                "RecurringExpensesApplied {Users} {Expenses}",
                results.Count,
                results.Sum(result => result.Generated.Count));

            await notifier.NotifyAsync(results, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down; the next start retries anything not yet generated.
        }
        catch (Exception exception)
        {
            // A failed pass must never take the service down; it is retried on the next tick.
            logger.LogError(exception, "RecurringExpenseApplicationFailed");
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
