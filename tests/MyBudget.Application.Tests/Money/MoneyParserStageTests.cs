using FluentAssertions;
using MyBudget.Application.Money;

namespace MyBudget.Application.Tests.Money;

/// <summary>
/// The parsing stages, exercised directly.
/// <para>
/// These cover the guards that the public pipeline cannot reach because earlier stages
/// already reject the input. They exist as defence for future callers, so they are tested
/// rather than left to rot.
/// </para>
/// </summary>
public sealed class MoneyParserStageTests
{
    private static readonly CurrencyDefinition Cop = CurrencyDefinition.ColombianPeso;

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void The_suffix_resolver_rejects_input_that_is_not_an_amount(string numericPart)
    {
        MagnitudeSuffixResolver.TryResolve(numericPart, Cop, out _, out _, out var error).Should().BeFalse();
        error.Should().Be(MoneyParseError.NotANumber);
    }

    [Fact]
    public void The_suffix_resolver_rejects_a_suffix_with_nothing_before_it()
    {
        // Reachable only when called directly: the normalizer trims and collapses first.
        MagnitudeSuffixResolver.TryResolve(" mil", Cop, out _, out _, out var error).Should().BeFalse();
        error.Should().Be(MoneyParseError.NotANumber);
    }

    [Fact]
    public void The_suffix_resolver_leaves_a_string_without_a_suffix_alone()
    {
        MagnitudeSuffixResolver.TryResolve("35.000", Cop, out var numericPart, out var multiplier, out _)
            .Should().BeTrue();

        numericPart.Should().Be("35.000");
        multiplier.Should().Be(1);
    }

    [Fact]
    public void The_separator_resolver_rejects_an_empty_numeric_part()
    {
        var resolution = SeparatorResolver.Resolve(string.Empty, Cop);

        resolution.Outcome.Should().Be(SeparatorOutcome.Failed);
        resolution.Error.Should().Be(MoneyParseError.NotANumber);
    }

    [Fact]
    public void The_separator_resolver_rejects_a_repeated_decimal_separator()
    {
        // "1.234,56,78": the last separator is the decimal one, and it appears twice.
        var resolution = SeparatorResolver.Resolve("1.234,56,78", Cop);

        resolution.Outcome.Should().Be(SeparatorOutcome.Failed);
        resolution.Error.Should().Be(MoneyParseError.MalformedGrouping);
    }

    [Fact]
    public void A_currency_with_decimal_places_reports_the_fractional_reading_when_grouping_is_invalid()
    {
        // "1000.000" is not valid thousands grouping (a leading group of four digits), but it
        // is a valid decimal for a currency with decimal places. Exactly one reading survives,
        // so the parser must not ask the user anything.
        var result = TestServices.MoneyParser.Parse("1000.000", TestServices.TwoDecimalCurrency);

        result.Should().BeOfType<MoneyParseResult.Success>();
        ((MoneyParseResult.Success)result).Amount.Should().Be(100_000);
    }

    [Fact]
    public void A_currency_with_decimal_places_rejects_a_reading_it_cannot_represent()
    {
        // "1.005" EUR: the grouping reading is 1005 EUR, the fractional reading is 1,005 EUR
        // which is not a whole number of cents. Only the grouping reading survives.
        var result = TestServices.MoneyParser.Parse("1.005", TestServices.TwoDecimalCurrency);

        result.Should().BeOfType<MoneyParseResult.Success>();
        ((MoneyParseResult.Success)result).Amount.Should().Be(100_500);
    }

    [Fact]
    public void A_currency_with_decimal_places_rejects_both_readings_when_both_are_too_large()
    {
        var result = TestServices.MoneyParser.Parse("999.999.999.999.000", TestServices.TwoDecimalCurrency);

        result.Should().BeOfType<MoneyParseResult.Invalid>();
        ((MoneyParseResult.Invalid)result).Reason.Should().Be(MoneyParseError.TooLarge);
    }

    [Fact]
    public void A_currency_with_decimal_places_rejects_input_that_fails_both_readings_structurally()
    {
        // Sixteen digits before the separator is not valid grouping, and nineteen digits in
        // total is beyond what the parser will consider.
        var result = TestServices.MoneyParser.Parse("1234567890123456.789", TestServices.TwoDecimalCurrency);

        result.Should().BeOfType<MoneyParseResult.Invalid>();
        ((MoneyParseResult.Invalid)result).Reason.Should().Be(MoneyParseError.MalformedGrouping);
    }

    [Theory]
    [InlineData("35\u2009000")]
    [InlineData("35\u2007000")]
    [InlineData("35\t000")]
    public void Every_space_like_character_a_keyboard_can_send_is_treated_as_grouping(string input)
    {
        var result = TestServices.MoneyParser.Parse(input, Cop);

        result.Should().BeOfType<MoneyParseResult.Success>();
        ((MoneyParseResult.Success)result).Amount.Should().Be(35000);
    }

    [Fact]
    public void A_message_that_is_only_a_count_and_an_amount_has_no_description()
    {
        var result = TestServices.CompactParser.Parse("2 x 3.500", Cop);

        result.Outcome.Should().Be(CompactExpenseOutcome.Parsed);
        result.Amount.Should().Be(3_500);
        result.Description.Should().BeNull();
    }
}
