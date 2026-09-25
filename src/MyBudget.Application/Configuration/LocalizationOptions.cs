namespace MyBudget.Application.Configuration;

/// <summary>
/// Localization defaults applied to new users and used when formatting.
/// </summary>
public sealed class LocalizationOptions
{
    public const string SectionName = "Localization";

    /// <summary>BCP-47-ish tag, for example <c>es</c>. Spanish is the only shipped language today.</summary>
    public string DefaultLanguage { get; set; } = "es";

    /// <summary>IANA time zone identifier, for example <c>America/Bogota</c>.</summary>
    public string DefaultTimeZone { get; set; } = "America/Bogota";
}
