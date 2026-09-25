using MyBudget.Domain.Common;

namespace MyBudget.Domain.Users;

/// <summary>
/// A person using the bot. Identity is the Telegram user id, never the username:
/// usernames are mutable and optional.
/// </summary>
public sealed class User : Entity
{
    public const string DefaultCurrency = "COP";
    public const string DefaultTimeZone = "America/Bogota";
    public const string DefaultLanguage = "es";
    public const int MaxUsernameLength = 64;
    public const int MaxDisplayNameLength = 128;
    public const int MaxTimeZoneLength = 64;
    public const int MaxLanguageLength = 5;

    private User()
    {
    }

    public User(long telegramUserId, string? username = null, string? displayName = null)
    {
        if (telegramUserId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(telegramUserId), telegramUserId, "Telegram user id must be positive.");
        }

        TelegramUserId = telegramUserId;
        Currency = DefaultCurrency;
        TimeZone = DefaultTimeZone;
        Language = DefaultLanguage;
        UpdateProfile(username, displayName);
    }

    public long TelegramUserId { get; private set; }

    public string? Username { get; private set; }

    public string? DisplayName { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;

    public string TimeZone { get; private set; } = DefaultTimeZone;

    public string Language { get; private set; } = DefaultLanguage;

    /// <summary>
    /// Refreshes the profile snapshot from Telegram. Both values are display-only and
    /// must never be used as identity.
    /// </summary>
    public void UpdateProfile(string? username, string? displayName)
    {
        Username = Truncate(username, MaxUsernameLength);
        DisplayName = Truncate(displayName, MaxDisplayNameLength);
    }

    public void ChangeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a 3 letter ISO 4217 code.", nameof(currency));
        }

        Currency = currency.Trim().ToUpperInvariant();
    }

    public void ChangeTimeZone(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Trim().Length > MaxTimeZoneLength)
        {
            throw new ArgumentException("Time zone is required and must be a valid IANA identifier.", nameof(timeZone));
        }

        TimeZone = timeZone.Trim();
    }

    public void ChangeLanguage(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Trim().Length > MaxLanguageLength)
        {
            throw new ArgumentException("Language is required.", nameof(language));
        }

        Language = language.Trim();
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
