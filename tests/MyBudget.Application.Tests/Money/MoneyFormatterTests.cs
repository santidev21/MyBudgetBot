using FluentAssertions;
using MyBudget.Application.Money;

namespace MyBudget.Application.Tests.Money;

public sealed class MoneyFormatterTests
{
    private static string Format(long amount) => TestServices.Formatter.Format(amount, "COP");

    [Theory]
    [InlineData(0, "$0")]
    [InlineData(5, "$5")]
    [InlineData(999, "$999")]
    [InlineData(1000, "$1.000")]
    [InlineData(3500, "$3.500")]
    [InlineData(35000, "$35.000")]
    [InlineData(150000, "$150.000")]
    [InlineData(1500000, "$1.500.000")]
    [InlineData(1234567890, "$1.234.567.890")]
    [InlineData(999999999999, "$999.999.999.999")]
    public void Formats_whole_pesos_with_colombian_grouping(long amount, string expected)
    {
        // The exact strings from the specification, pinned so a culture or ICU change on the
        // host cannot alter what a user sees.
        Format(amount).Should().Be(expected);
    }

    [Fact]
    public void Formats_negative_amounts_for_remaining_budget_display()
    {
        Format(-150_000).Should().Be("-$150.000");
    }

    [Theory]
    [InlineData(0, "0 %")]
    [InlineData(50, "50 %")]
    [InlineData(72, "72 %")]
    [InlineData(60.6, "60,6 %")]
    [InlineData(115, "115 %")]
    [InlineData(33.333, "33,3 %")]
    public void Formats_percentages_with_a_comma_and_no_trailing_zero(decimal percentage, string expected)
    {
        TestServices.Formatter.FormatPercentage(percentage).Should().Be(expected);
    }

    [Fact]
    public void Formats_a_currency_with_decimal_places()
    {
        TestServices.Formatter.Format(150_025, TestServices.TwoDecimalCurrency).Should().Be("€1.500,25");
        TestServices.Formatter.Format(150_000, TestServices.TwoDecimalCurrency).Should().Be("€1.500,00");
        TestServices.Formatter.Format(5, TestServices.TwoDecimalCurrency).Should().Be("€0,05");
    }

    [Fact]
    public void Formats_by_currency_code()
    {
        TestServices.Formatter.Format(35_000, "COP").Should().Be("$35.000");
        TestServices.Formatter.Format(35_000, "cop").Should().Be("$35.000");
    }

    [Fact]
    public void An_unregistered_currency_is_rejected()
    {
        var act = () => TestServices.Formatter.Format(1, "XYZ");

        act.Should().Throw<UnsupportedCurrencyException>();
    }
}
