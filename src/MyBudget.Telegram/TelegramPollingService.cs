using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBudget.Telegram.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace MyBudget.Telegram;

/// <summary>
/// Long polling, for local development only.
/// <para>
/// Production uses webhooks, and Telegram allows only one of the two at a time. This exists so
/// the bot can be exercised from a laptop with no public URL. It reuses the exact same
/// dispatcher, so the pipeline under test is the pipeline that ships.
/// </para>
/// </summary>
internal sealed class TelegramPollingService(
    ITelegramBotClient botClient,
    ITelegramUpdateDispatcher dispatcher,
    IOptions<TelegramOptions> options,
    ILogger<TelegramPollingService> logger) : BackgroundService
{
    private static readonly UpdateType[] AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.UsePolling)
        {
            return;
        }

        logger.LogInformation("TelegramPollingStarted");
        var offset = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await botClient.GetUpdates(
                    offset: offset,
                    limit: 10,
                    timeout: 30,
                    allowedUpdates: AllowedUpdates,
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    // Advance before processing so a failing update is not fetched forever.
                    offset = update.Id + 1;
                    await dispatcher.DispatchAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                // A network blip must not kill the loop; back off briefly and continue.
                logger.LogError(exception, "TelegramPollingFailed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
