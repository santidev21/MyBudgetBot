using FluentAssertions;
using MyBudget.Application.Configuration;

namespace MyBudget.Application.Tests.Configuration;

public sealed class LocalizationOptionsValidatorTests
{
    private readonly LocalizationOptionsValidator _validator = new();

    [Fact]
    public void The_shipped_defaults_are_valid()
    {
        var result = _validator.Validate(null, new LocalizationOptions());

        result.Succeeded.Should().BeTrue();
        result.Failures.Should().BeNull();
    }

    [Theory]
    [InlineData("es")]
    [InlineData("es-CO")]
    [InlineData("en")]
    public void Well_formed_language_tags_are_accepted(string language)
    {
        var options = new LocalizationOptions
        {
            DefaultLanguage = language,
            SupportedLanguages = [language],
        };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("spanish")]
    [InlineData("E")]
    [InlineData("ES")]
    [InlineData("es-co")]
    [InlineData("es_CO")]
    public void Malformed_language_tags_are_rejected(string language)
    {
        var options = new LocalizationOptions
        {
            DefaultLanguage = language,
            SupportedLanguages = [language],
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("DefaultLanguage"));
    }

    [Theory]
    [InlineData("America/Bogota")]
    [InlineData("UTC")]
    [InlineData("America/Mexico_City")]
    public void Known_time_zone_identifiers_are_accepted(string timeZone)
    {
        var options = new LocalizationOptions { DefaultTimeZone = timeZone };

        _validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bogota")]
    [InlineData("America/Bogata")]
    [InlineData("-05:00")]
    public void Unknown_time_zone_identifiers_are_rejected(string timeZone)
    {
        // A bad identifier would otherwise only surface when the first user is created,
        // and then break every date calculation for that user.
        var options = new LocalizationOptions { DefaultTimeZone = timeZone };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("DefaultTimeZone"));
    }

    [Fact]
    public void An_empty_supported_language_list_is_rejected()
    {
        var options = new LocalizationOptions { SupportedLanguages = [] };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("SupportedLanguages"));
    }

    [Fact]
    public void A_malformed_supported_language_is_rejected()
    {
        var options = new LocalizationOptions { SupportedLanguages = ["es", "english"] };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("english"));
    }

    [Fact]
    public void A_default_language_without_resources_is_rejected()
    {
        // Otherwise every message would render as a raw key like "Expense.Registered".
        var options = new LocalizationOptions
        {
            DefaultLanguage = "en",
            SupportedLanguages = ["es"],
        };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("SupportedLanguages"));
    }

    [Fact]
    public void The_default_language_matches_ignoring_case()
    {
        var options = new LocalizationOptions
        {
            DefaultLanguage = "es-CO",
            SupportedLanguages = ["es-co"],
        };

        // "es-co" is a malformed tag, so this is rejected for that reason, not for the match.
        _validator.Validate(null, options).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void Both_failures_are_reported_together()
    {
        var options = new LocalizationOptions
        {
            DefaultLanguage = "nope",
            DefaultTimeZone = "Nowhere/Fake",
        };

        var result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().HaveCount(2);
    }
}
