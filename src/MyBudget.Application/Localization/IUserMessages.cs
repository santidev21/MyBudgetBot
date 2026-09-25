namespace MyBudget.Application.Localization;

/// <summary>
/// The single source of user-facing text.
/// <para>
/// The language is an explicit parameter, never ambient state. A Telegram bot has no HTTP
/// request culture, so anything reading <c>CultureInfo.CurrentUICulture</c> would depend on
/// async-local state being set correctly at every entry point and would fail silently. This
/// interface cannot fail that way.
/// </para>
/// </summary>
public interface IUserMessages
{
    IReadOnlyCollection<string> SupportedLanguages { get; }

    /// <summary>
    /// Resolves a message, falling back to the configured default language and finally to
    /// the key itself, so a user never sees a blank message. Never throws.
    /// </summary>
    string Get(string language, string key, params object?[] args);

    bool Contains(string language, string key);
}
