using Microsoft.Extensions.Options;

namespace MyBudget.Telegram.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;

    /// <summary>Echoed by Telegram in a header on every webhook call. Validated in constant time.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Random URL segment, so the webhook path is not guessable on its own.</summary>
    public string WebhookPath { get; set; } = string.Empty;

    /// <summary>Public HTTPS base URL, for example <c>https://mybudget.santidev21.tech</c>. Used to register the webhook.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The only users allowed to talk to the bot. Fails closed: an empty list refuses everyone.
    /// </summary>
    public long[] AllowedUserIds { get; set; } = [];

    /// <summary>
    /// Updates older than this are ignored. Telegram replays queued updates after downtime, and
    /// acting on a day-old "35.000" would create garbage.
    /// </summary>
    public int StaleUpdateMinutes { get; set; } = 15;

    public int ConversationTimeoutMinutes { get; set; } = 30;

    /// <summary>Long polling for local development. Off in production, where webhooks are used.</summary>
    public bool UsePolling { get; set; }

    /// <summary>
    /// Telegram is wired up only when a token is present, so the service can still run for
    /// migrations and health checks without credentials.
    /// </summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(BotToken);

    public bool IsUserAllowed(long telegramUserId) => AllowedUserIds.Contains(telegramUserId);

    public TimeSpan StaleUpdateWindow => TimeSpan.FromMinutes(Math.Max(1, StaleUpdateMinutes));

    public TimeSpan ConversationTimeout => TimeSpan.FromMinutes(Math.Max(1, ConversationTimeoutMinutes));
}

/// <summary>
/// Validates Telegram configuration at startup.
/// <para>
/// Telegram is optional, but half-configured is not: a token without a secret and an allowlist
/// would expose an unauthenticated endpoint or an open bot.
/// </para>
/// </summary>
public sealed class TelegramOptionsValidator : IValidateOptions<TelegramOptions>
{
    public ValidateOptionsResult Validate(string? name, TelegramOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsEnabled)
        {
            // Without a token the service runs for migrations and health checks only. Setting a
            // webhook secret or path without a token is still a mistake worth failing on.
            var partiallyConfigured = !string.IsNullOrWhiteSpace(options.WebhookSecret)
                                      || !string.IsNullOrWhiteSpace(options.WebhookPath);

            return partiallyConfigured
                ? ValidateOptionsResult.Fail(
                    $"{TelegramOptions.SectionName}:BotToken is required when any other Telegram setting is set.")
                : ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (options.WebhookSecret.Trim().Length < 16)
        {
            failures.Add($"{TelegramOptions.SectionName}:WebhookSecret must be at least 16 characters.");
        }

        if (options.WebhookPath.Trim().Length < 16)
        {
            failures.Add($"{TelegramOptions.SectionName}:WebhookPath must be at least 16 characters.");
        }

        if (options.AllowedUserIds.Length == 0)
        {
            failures.Add(
                $"{TelegramOptions.SectionName}:AllowedUserIds must list at least one Telegram user id. " +
                "Without it the bot would be open to anyone who finds it.");
        }
        else if (options.AllowedUserIds.Any(id => id <= 0))
        {
            failures.Add($"{TelegramOptions.SectionName}:AllowedUserIds must contain positive ids.");
        }

        if (options.StaleUpdateMinutes < 1)
        {
            failures.Add($"{TelegramOptions.SectionName}:StaleUpdateMinutes must be at least 1.");
        }

        if (options.ConversationTimeoutMinutes < 1)
        {
            failures.Add($"{TelegramOptions.SectionName}:ConversationTimeoutMinutes must be at least 1.");
        }

        if (!options.UsePolling && string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            failures.Add(
                $"{TelegramOptions.SectionName}:PublicBaseUrl is required when the webhook is the transport. " +
                "It is not needed when Telegram:UsePolling is true.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
