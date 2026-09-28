using System.Globalization;
using Microsoft.Extensions.Options;

namespace MyBudget.Telegram.Options;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    private IReadOnlyList<long>? _allowedUserIds;

    public string BotToken { get; set; } = string.Empty;

    /// <summary>Echoed by Telegram in a header on every webhook call. Validated in constant time.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Random URL segment, so the webhook path is not guessable on its own.</summary>
    public string WebhookPath { get; set; } = string.Empty;

    /// <summary>Public HTTPS base URL, for example <c>https://mybudget.santidev21.tech</c>. Used to register the webhook.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Telegram user ids allowed to use the bot, comma separated: <c>111,222,333</c>.
    /// <para>
    /// Kept as a string on purpose. Configuration binding does not turn a single value into an
    /// array: a bound <c>long[]</c> silently stays empty, and because access fails closed the
    /// bot would refuse every user including its owner. Parsing is explicit here and validated
    /// at startup.
    /// </para>
    /// </summary>
    public string AllowedUserIds { get; set; } = string.Empty;

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

    /// <summary>
    /// Whether this Telegram user may use the bot. Fails closed: an empty or unparsable list
    /// refuses everyone, and the startup validator refuses to let that reach production.
    /// </summary>
    public bool IsUserAllowed(long telegramUserId) => AllowedUserIdsList.Contains(telegramUserId);

    /// <summary>The configured ids, parsed once.</summary>
    public IReadOnlyList<long> AllowedUserIdsList => _allowedUserIds ??= ParseAllowedUserIds();

    public TimeSpan StaleUpdateWindow => TimeSpan.FromMinutes(Math.Max(1, StaleUpdateMinutes));

    public TimeSpan ConversationTimeout => TimeSpan.FromMinutes(Math.Max(1, ConversationTimeoutMinutes));

    /// <summary>Parses a comma separated list, tolerating spaces and trailing separators.</summary>
    public static bool TryParseAllowedUserIds(
        string? value, out IReadOnlyList<long> ids, out string? error)
    {
        ids = [];
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parsed = new List<long>();

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                ids = [];
                error = $"'{part}' is not a valid Telegram user id.";
                return false;
            }

            parsed.Add(id);
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        ids = parsed;
        return true;
    }

    private IReadOnlyList<long> ParseAllowedUserIds() =>
        TryParseAllowedUserIds(AllowedUserIds, out var ids, out _) ? ids : [];
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

        if (!TelegramOptions.TryParseAllowedUserIds(options.AllowedUserIds, out _, out var allowedError))
        {
            failures.Add(allowedError is null
                ? $"{TelegramOptions.SectionName}:AllowedUserIds must list at least one Telegram user id, " +
                  "comma separated. Without it the bot would be open to anyone who finds it."
                : $"{TelegramOptions.SectionName}:AllowedUserIds {allowedError} Expected a comma separated " +
                  "list of numeric ids, for example 111,222.");
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
