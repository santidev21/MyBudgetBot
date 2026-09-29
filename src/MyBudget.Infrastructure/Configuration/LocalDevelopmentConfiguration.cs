using Microsoft.Extensions.Configuration;

namespace MyBudget.Infrastructure.Configuration;

/// <summary>
/// Loads <c>.env</c> for local development so the native and Docker paths configure the
/// service from the same file.
/// <para>
/// The mapping below mirrors what <c>docker-compose.yml</c> passes to the app container.
/// <c>.env</c> uses friendly names because Compose, scripts and humans read it; .NET wants
/// <c>Section:Key</c>. Keeping the translation in one tested place is what stops the two
/// ways of running the service from drifting.
/// </para>
/// </summary>
public static class LocalDevelopmentConfiguration
{
    /// <summary>
    /// <c>.env</c> variable name to .NET configuration key. Every variable documented in
    /// <c>.env.example</c> is either here or listed in <see cref="ComposeOnlyVariables"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> KeyMap { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DATABASE_CONNECTION_STRING"] = "ConnectionStrings:Database",
            ["TELEGRAM_BOT_TOKEN"] = "Telegram:BotToken",
            ["TELEGRAM_WEBHOOK_SECRET"] = "Telegram:WebhookSecret",
            ["TELEGRAM_WEBHOOK_PATH"] = "Telegram:WebhookPath",
            ["TELEGRAM_PUBLIC_BASE_URL"] = "Telegram:PublicBaseUrl",
            ["ALLOWED_TELEGRAM_USER_IDS"] = "Telegram:AllowedUserIds",
            ["TELEGRAM_USE_POLLING"] = "Telegram:UsePolling",
            ["TELEGRAM_STALE_UPDATE_MINUTES"] = "Telegram:StaleUpdateMinutes",
            ["TELEGRAM_CONVERSATION_TIMEOUT_MINUTES"] = "Telegram:ConversationTimeoutMinutes",
            ["TELEGRAM_USER_RATE_LIMIT_PER_MINUTE"] = "Telegram:UserRateLimitPerMinute",
            ["DEFAULT_LANGUAGE"] = "Localization:DefaultLanguage",
            ["DEFAULT_TIME_ZONE"] = "Localization:DefaultTimeZone",
        };

    /// <summary>
    /// Variables that exist only to be consumed by Compose when it builds the container's
    /// environment, and therefore never reach the application directly.
    /// </summary>
    public static IReadOnlyCollection<string> ComposeOnlyVariables { get; } =
    [
        "POSTGRES_DB",
        "POSTGRES_USER",
        "POSTGRES_PASSWORD",
        "DB_APP_USER",
        "DB_APP_PASSWORD",
        "DB_MIGRATOR_USER",
        "DB_MIGRATOR_PASSWORD",
        "ADMIN_TELEGRAM_USER_ID",
        "BACKUP_RETENTION_DAYS",
        "BACKUP_OFFSITE_TARGET",
        "BACKUP_AGE_RECIPIENT",
    ];

    /// <summary>
    /// Resolves <c>.env</c> into configuration keys, skipping anything the environment
    /// already provides: an explicitly set variable always wins over the file.
    /// <para>
    /// A blank value counts as not provided. <c>appsettings.json</c> ships
    /// <c>ConnectionStrings:Database</c> as an empty string, and treating that as a real value
    /// would stop <c>.env</c> from ever filling it in.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Resolve(
        IConfiguration configuration, string? startDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var fromFile = DotEnvFile.Load(startDirectory);
        var resolved = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (name, value) in fromFile)
        {
            if (!KeyMap.TryGetValue(name, out var key) || !string.IsNullOrWhiteSpace(configuration[key]))
            {
                continue;
            }

            resolved[key] = value;
        }

        return resolved;
    }
}
