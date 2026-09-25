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
        var options = new LocalizationOptions { DefaultLanguage = language };

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
        var options = new LocalizationOptions { DefaultLanguage = language };

        var result = _validator.Validate(null, options);

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().ContainSingle(failure => failure.Contains("DefaultLanguage"));
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
        result.Failures.Should().ContainSingle(failure => failure.Contains("DefaultTimeZone"));
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
