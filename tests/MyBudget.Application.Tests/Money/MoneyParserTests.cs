using FluentAssertions;
using MyBudget.Application.Money;

namespace MyBudget.Application.Tests.Money;

public sealed class MoneyParserTests
{
    private static MoneyParseResult Parse(string? input) => TestServices.MoneyParser.Parse(input, "COP");

    private static long AmountOf(string input)
    {
        var result = Parse(input);
        result.Should().BeOfType<MoneyParseResult.Success>($"'{input}' should parse");
        return ((MoneyParseResult.Success)result).Amount;
    }

    [Theory]
    // Plain digits.
    [InlineData("35000", 35000)]
    [InlineData("150000", 150_000)]
    [InlineData("1500000", 1_500_000)]
    [InlineData("  35000  ", 35000)]
    // Colombian grouping.
    [InlineData("35.000", 35000)]
    [InlineData("1.500.000", 1_500_000)]
    [InlineData("1.234.567.890", 1_234_567_890)]
    // Tolerated thousands separator.
    [InlineData("35,000", 35000)]
    [InlineData("1,234,567", 1_234_567)]
    // Spaces, including the non-breaking and thin variants mobile keyboards send.
    [InlineData("35 000", 35000)]
    [InlineData("35\u00A0000", 35000)]
    [InlineData("35\u202F000", 35000)]
    [InlineData("1 234 567", 1_234_567)]
    // Currency symbols and words.
    [InlineData("$35.000", 35000)]
    [InlineData("$ 35.000", 35000)]
    [InlineData("35000 pesos", 35000)]
    [InlineData("35000pesos", 35000)]
    [InlineData("$35,000 pesos", 35000)]
    [InlineData("COP 35.000", 35000)]
    [InlineData("cop 35.000", 35000)]
    [InlineData("+35.000", 35000)]
    [InlineData("35.000.", 35000)]
    // Magnitude suffixes.
    [InlineData("35k", 35000)]
    [InlineData("35K", 35000)]
    [InlineData("35 k", 35000)]
    [InlineData("35 mil", 35000)]
    [InlineData("35mil", 35000)]
    [InlineData("60k", 60_000)]
    [InlineData("1.5k", 1_500)]
    [InlineData("1,5k", 1_500)]
    [InlineData("2 millones", 2_000_000)]
    [InlineData("1.5 millones", 1_500_000)]
    [InlineData("1,5 millones", 1_500_000)]
    [InlineData("1,5 millones de pesos", 1_500_000)]
    [InlineData("35 mm", 35_000_000)]
    // Leading zero groups are lenient, not dangerous: there is no other valid reading.
    [InlineData("0.500", 500)]
    // Bounds.
    [InlineData("1", 1)]
    [InlineData("999999999999", 999_999_999_999)]
    public void Parses_the_amounts_users_actually_type(string input, long expected)
    {
        AmountOf(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("35000", MoneyInputForm.Plain)]
    [InlineData("35.000", MoneyInputForm.StandardGrouping)]
    [InlineData("35,000", MoneyInputForm.ToleratedGrouping)]
    [InlineData("35 000", MoneyInputForm.SpaceGrouped)]
    [InlineData("35k", MoneyInputForm.SuffixScaled)]
    [InlineData("1,5 millones", MoneyInputForm.SuffixScaled)]
    public void Records_how_the_user_wrote_the_amount(string input, MoneyInputForm expected)
    {
        var result = Parse(input);

        result.Should().BeOfType<MoneyParseResult.Success>();
        ((MoneyParseResult.Success)result).Form.Should().Be(expected);
    }

    [Theory]
    [InlineData("3500")]
    [InlineData("35.000")]
    [InlineData("35,000")]
    [InlineData("35 000")]
    [InlineData("$35.000")]
    [InlineData("35k")]
    [InlineData("35 mil")]
    [InlineData("1500000")]
    [InlineData("1.500.000")]
    public void Always_reports_a_colombian_display_value(string input)
    {
        var result = Parse(input);

        result.Should().BeOfType<MoneyParseResult.Success>();
        var success = (MoneyParseResult.Success)result;
        success.Display.Should().StartWith("$");
        success.Display.Should().NotContain(" ");
    }

    [Fact]
    public void Formats_the_normalised_value_for_the_confirmation_screen()
    {
        AmountOf("3500").Should().Be(3_500);
        ((MoneyParseResult.Success)Parse("3500")).Display.Should().Be("$3.500");
        ((MoneyParseResult.Success)Parse("35k")).Display.Should().Be("$35.000");
        ((MoneyParseResult.Success)Parse("1,5 millones")).Display.Should().Be("$1.500.000");
    }

    [Theory]
    [InlineData(null, MoneyParseError.Empty)]
    [InlineData("", MoneyParseError.Empty)]
    [InlineData("   ", MoneyParseError.Empty)]
    [InlineData("$", MoneyParseError.Empty)]
    [InlineData("abc", MoneyParseError.NotANumber)]
    [InlineData("verduras", MoneyParseError.NotANumber)]
    [InlineData("mil", MoneyParseError.NotANumber)]
    [InlineData("k", MoneyParseError.NotANumber)]
    [InlineData("35 mil mil", MoneyParseError.NotANumber)]
    [InlineData("1.5m", MoneyParseError.NotANumber)]
    [InlineData("35,000 kg", MoneyParseError.NotANumber)]
    [InlineData("-35.000", MoneyParseError.Negative)]
    [InlineData("0", MoneyParseError.NonPositive)]
    [InlineData("0.000", MoneyParseError.NonPositive)]
    [InlineData("3,5", MoneyParseError.FractionNotAllowed)]
    [InlineData("3,50", MoneyParseError.FractionNotAllowed)]
    [InlineData("1.50", MoneyParseError.FractionNotAllowed)]
    [InlineData("1.234,56", MoneyParseError.FractionNotAllowed)]
    [InlineData("1,234.56", MoneyParseError.FractionNotAllowed)]
    [InlineData("1.234.56", MoneyParseError.MalformedGrouping)]
    [InlineData("35.00.00.0", MoneyParseError.MalformedGrouping)]
    [InlineData("1000.000", MoneyParseError.MalformedGrouping)]
    [InlineData("1.1234", MoneyParseError.MalformedGrouping)]
    [InlineData("1000000000000", MoneyParseError.TooLarge)]
    [InlineData("1.234.567.890.123", MoneyParseError.TooLarge)]
    [InlineData("1234567890123456789012345", MoneyParseError.TooLarge)]
    public void Rejects_input_it_cannot_represent_exactly(string? input, MoneyParseError expected)
    {
        var result = Parse(input);

        result.Should().BeOfType<MoneyParseResult.Invalid>();
        ((MoneyParseResult.Invalid)result).Reason.Should().Be(expected);
    }

    [Fact]
    public void A_fraction_the_currency_cannot_express_is_rejected_rather_than_rounded()
    {
        // 1.500,25 COP is 1500 pesos and 25 centavos. COP has no cents, and silently
        // rounding someone's money is not acceptable.
        var result = TestServices.MoneyParser.Parse("1.500,25", "COP");

        result.Should().BeOfType<MoneyParseResult.Invalid>();
        ((MoneyParseResult.Invalid)result).Reason.Should().Be(MoneyParseError.FractionNotAllowed);
    }

    [Fact]
    public void Three_digit_grouping_is_never_treated_as_ambiguous_for_cop()
    {
        // "3.500" is 3500. The alternative reading, 3,5 pesos, is not a whole peso value, so
        // there is nothing to ask about. This is why COP users are never interrupted for the
        // most common way of writing an amount.
        Parse("3.500").Should().BeOfType<MoneyParseResult.Success>();
        Parse("3,500").Should().BeOfType<MoneyParseResult.Success>();
    }

    [Fact]
    public void A_currency_with_decimal_places_does_report_the_genuine_ambiguity()
    {
        // 1.500 EUR could be 1500 euros or 1,50 euros. Both are real amounts, so the parser
        // refuses to choose. Unreachable for COP, but the architecture must not need a
        // rewrite to support it.
        var result = TestServices.MoneyParser.Parse("1.500", TestServices.TwoDecimalCurrency);

        result.Should().BeOfType<MoneyParseResult.Ambiguous>();
        var ambiguous = (MoneyParseResult.Ambiguous)result;
        ambiguous.Reason.Should().Be(MoneyAmbiguityReason.SeparatorRoleUnclear);
        ambiguous.Candidates.Should().BeEquivalentTo([150_000L, 150L]);
    }

    [Fact]
    public void Decimal_places_are_scaled_into_minor_units()
    {
        TestServices.MoneyParser.Parse("1.500,25", TestServices.TwoDecimalCurrency)
            .Should().BeOfType<MoneyParseResult.Success>()
            .Which.Amount.Should().Be(150_025);
    }

    [Fact]
    public void TryParse_reports_success_without_an_exception()
    {
        TestServices.MoneyParser.TryParse("35.000", "COP", out var amount).Should().BeTrue();
        amount.Should().Be(35000);

        TestServices.MoneyParser.TryParse("no soy un valor", "COP", out var invalid).Should().BeFalse();
        invalid.Should().Be(0);
    }

    [Fact]
    public void An_unregistered_currency_is_a_programming_error_not_a_parse_failure()
    {
        var act = () => TestServices.MoneyParser.Parse("35.000", "XYZ");

        act.Should().Throw<UnsupportedCurrencyException>();
    }

    [Fact]
    public void The_accepted_maximum_matches_the_database_constraint()
    {
        // ck_expenses_amount rejects anything above 999999999999. The parser must agree, or
        // the user would get a message about a database error.
        AmountOf("999999999999").Should().Be(MyBudget.Domain.Common.MoneyLimits.MaxAmount);
        Parse("1000000000000").Should().BeOfType<MoneyParseResult.Invalid>();
    }

    [Fact]
    public void The_input_is_preserved_in_the_failure_so_the_message_can_quote_it()
    {
        var result = (MoneyParseResult.Invalid)Parse("  no soy un valor  ");

        result.Input.Should().Be("  no soy un valor  ");
    }
}
