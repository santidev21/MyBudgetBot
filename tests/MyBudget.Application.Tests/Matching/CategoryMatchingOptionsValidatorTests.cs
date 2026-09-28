using FluentAssertions;
using MyBudget.Application.Matching;

namespace MyBudget.Application.Tests.Matching;

/// <summary>
/// The matching policy is validated at startup, so a nonsensical threshold fails the boot
/// instead of quietly changing how the bot guesses a category.
/// </summary>
public sealed class CategoryMatchingOptionsValidatorTests
{
    private readonly CategoryMatchingOptionsValidator _validator = new();

    [Fact]
    public void The_defaults_are_valid()
    {
        _validator.Validate(null, new CategoryMatchingOptions()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void A_minimum_score_outside_zero_and_one_is_rejected(double value)
    {
        var result = _validator.Validate(null, new CategoryMatchingOptions { MinimumScore = value });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("MinimumScore"));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void An_ambiguity_margin_outside_zero_and_one_is_rejected(double value)
    {
        var result = _validator.Validate(null, new CategoryMatchingOptions { AmbiguityMargin = value });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("AmbiguityMargin"));
    }

    [Fact]
    public void A_fuzzy_token_length_below_one_is_rejected()
    {
        var result = _validator.Validate(
            null, new CategoryMatchingOptions { EnableFuzzy = true, FuzzyMinimumTokenLength = 0 });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("FuzzyMinimumTokenLength"));
    }

    [Fact]
    public void A_fuzzy_token_budget_below_one_is_rejected()
    {
        var result = _validator.Validate(
            null, new CategoryMatchingOptions { EnableFuzzy = true, FuzzyMaxTokens = 0 });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(message => message.Contains("FuzzyMaxTokens"));
    }
}
