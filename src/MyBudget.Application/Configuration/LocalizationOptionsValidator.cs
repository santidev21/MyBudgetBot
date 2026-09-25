using Microsoft.Extensions.Options;

namespace MyBudget.Application.Configuration;

/// <summary>
/// Validates the localization defaults at startup.
/// <para>
/// A bad time zone identifier would otherwise stay invisible until the first user is
/// created, and then every date calculation for that user would throw. Failing at boot is
/// the only acceptable behaviour.
/// </para>
/// </summary>
public sealed class LocalizationOptionsValidator : IValidateOptions<LocalizationOptions>
{
    public ValidateOptionsResult Validate(string? name, LocalizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.DefaultTimeZone, out _))
        {
            failures.Add(
                $"{LocalizationOptions.SectionName}:DefaultTimeZone '{options.DefaultTimeZone}' is not a " +
                "time zone known to this machine. Use an IANA identifier such as 'America/Bogota'.");
        }

        if (!IsValidLanguageTag(options.DefaultLanguage))
        {
            failures.Add(
                $"{LocalizationOptions.SectionName}:DefaultLanguage '{options.DefaultLanguage}' must look " +
                "like 'es' or 'es-CO'.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValidLanguageTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('-', 2);

        return parts[0].Length == 2
               && parts[0].All(char.IsAsciiLetterLower)
               && (parts.Length == 1
                   || (parts[1].Length == 2 && parts[1].All(char.IsAsciiLetterUpper)));
    }
}
