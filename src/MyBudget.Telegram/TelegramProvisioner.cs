using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace MyBudget.Telegram;

/// <summary>
/// Registers the bot with Telegram: the command menu and the webhook.
/// <para>
/// This is an explicit operation, never something that happens at startup. Calling
/// <c>setWebhook</c> on every boot would, with <c>dropPendingUpdates</c>, silently discard
/// updates the user is waiting on.
/// </para>
/// </summary>
public interface ITelegramProvisioner
{
    Task ConfigureAsync(bool dropPendingUpdates, CancellationToken cancellationToken = default);

    /// <summary>Removes the webhook, which is required before long polling can work.</summary>
    Task DeleteWebhookAsync(bool dropPendingUpdates, CancellationToken cancellationToken = default);
}

internal sealed class TelegramProvisioner(
    ITelegramBotClient botClient,
    IUserMessages messages,
    IOptions<TelegramOptions> options,
    ILogger<TelegramProvisioner> logger) : ITelegramProvisioner
{
    private static readonly UpdateType[] AllowedUpdates =
        [UpdateType.Message, UpdateType.CallbackQuery];

    public async Task ConfigureAsync(bool dropPendingUpdates, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;

        if (!settings.IsEnabled)
        {
            throw new InvalidOperationException("Telegram:BotToken is not configured.");
        }

        if (string.IsNullOrWhiteSpace(settings.PublicBaseUrl))
        {
            throw new InvalidOperationException("Telegram:PublicBaseUrl is not configured.");
        }

        // The command menu is localized by Telegram itself through language_code.
        await botClient.SetMyCommands(
            BotCommands.Catalog(
                messages.Get("es", MessageKeys.CommandStartDescription),
                messages.Get("es", MessageKeys.CommandHelpDescription),
                messages.Get("es", MessageKeys.CommandCancelDescription)),
            languageCode: "es",
            cancellationToken: cancellationToken);

        var url = $"{settings.PublicBaseUrl.TrimEnd('/')}{TelegramWebhook.BuildPath(settings.WebhookPath)}";

        await botClient.SetWebhook(
            url,
            maxConnections: 40,
            allowedUpdates: AllowedUpdates,
            dropPendingUpdates: dropPendingUpdates,
            secretToken: settings.WebhookSecret,
            cancellationToken: cancellationToken);

        // The URL contains the secret path segment, so only the public base is logged.
        logger.LogInformation("TelegramWebhookConfigured {PublicBaseUrl}", settings.PublicBaseUrl);
    }

    public async Task DeleteWebhookAsync(
        bool dropPendingUpdates, CancellationToken cancellationToken = default)
    {
        await botClient.DeleteWebhook(dropPendingUpdates, cancellationToken);
        logger.LogInformation("TelegramWebhookDeleted");
    }
}
