using FsCheck.Xunit;
using MyBudget.Application.Money;
using MyBudget.Domain.Common;

namespace MyBudget.Application.Tests.Money;

/// <summary>
/// Property-based checks. The example-based corpus covers what users type; these cover the
/// infinite space around it, which is where silent data corruption hides.
/// </summary>
public sealed class MoneyParserPropertyTests
{
    [Property(MaxTest = 500)]
    public bool Formatting_then_parsing_returns_the_same_amount(long seed)
    {
        var amount = Math.Abs(seed % MoneyLimits.MaxAmount) + 1;
        var display = TestServices.Formatter.Format(amount, "COP");

        return TestServices.MoneyParser.Parse(display, "COP") is MoneyParseResult.Success success
               && success.Amount == amount;
    }

    [Property(MaxTest = 500)]
    public bool Parsing_arbitrary_text_never_throws(string input)
    {
        var result = TestServices.MoneyParser.Parse(input, "COP");

        return result is MoneyParseResult.Success
            or MoneyParseResult.Ambiguous
            or MoneyParseResult.Invalid;
    }

    [Property(MaxTest = 500)]
    public bool A_successful_parse_is_always_a_positive_amount_within_bounds(long seed)
    {
        var amount = Math.Abs(seed % MoneyLimits.MaxAmount) + 1;
        var result = TestServices.MoneyParser.Parse(TestServices.Formatter.Format(amount, "COP"), "COP");

        return result is MoneyParseResult.Success success
               && success.Amount > 0
               && success.Amount <= MoneyLimits.MaxAmount;
    }

    [Property(MaxTest = 500)]
    public bool A_thousands_suffix_multiplies_by_a_thousand(long seed)
    {
        var amount = Math.Abs(seed % 100_000_000) + 1;
        var result = TestServices.MoneyParser.Parse($"{amount}k", "COP");

        return result is MoneyParseResult.Success success
               && success.Amount == amount * 1_000;
    }

    [Property(MaxTest = 500)]
    public bool Plain_digits_always_round_trip(long seed)
    {
        var amount = Math.Abs(seed % MoneyLimits.MaxAmount) + 1;

        return TestServices.MoneyParser.Parse(amount.ToString(System.Globalization.CultureInfo.InvariantCulture), "COP")
               is MoneyParseResult.Success success
               && success.Amount == amount;
    }
}
