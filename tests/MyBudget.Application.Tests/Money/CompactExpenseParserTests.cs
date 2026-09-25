using FluentAssertions;
using MyBudget.Application.Money;

namespace MyBudget.Application.Tests.Money;

public sealed class CompactExpenseParserTests
{
    private static CompactExpenseResult Parse(string? input) => TestServices.CompactParser.Parse(input, "COP");

    [Theory]
    [InlineData("35000 verduras", 35000, "verduras")]
    [InlineData("35.000 verduras", 35000, "verduras")]
    [InlineData("35,000 supermercado", 35000, "supermercado")]
    [InlineData("$35,000 supermercado", 35000, "supermercado")]
    [InlineData("$ 35.000 supermercado", 35000, "supermercado")]
    [InlineData("60k gasolina", 60_000, "gasolina")]
    [InlineData("35 mil arroz", 35000, "arroz")]
    [InlineData("35 000 pan", 35000, "pan")]
    [InlineData("1,5 millones arriendo", 1_500_000, "arriendo")]
    [InlineData("pagué 3.500 de frutas", 3_500, "frutas")]
    [InlineData("pague 3500 de frutas", 3_500, "frutas")]
    [InlineData("el almuerzo 25.000", 25_000, "almuerzo")]
    [InlineData("verduras 35.000", 35000, "verduras")]
    [InlineData("35.000 supermercado y verduras", 35000, "supermercado y verduras")]
    [InlineData("2 x 3500 almuerzo", 3_500, "almuerzo")]
    public void Extracts_an_amount_and_a_description_from_one_message(
        string input, long expectedAmount, string expectedDescription)
    {
        var result = Parse(input);

        result.Outcome.Should().Be(CompactExpenseOutcome.Parsed);
        result.Amount.Should().Be(expectedAmount);
        result.Description.Should().Be(expectedDescription);
    }

    [Theory]
    [InlineData("35.000", 35000)]
    [InlineData("35000", 35000)]
    [InlineData("60k", 60_000)]
    [InlineData("$35.000", 35000)]
    public void A_message_that_is_only_an_amount_answers_the_amount_prompt(string input, long expected)
    {
        var result = Parse(input);

        result.Outcome.Should().Be(CompactExpenseOutcome.AmountOnly);
        result.Amount.Should().Be(expected);
        result.Description.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("compré verduras")]
    [InlineData("verduras")]
    [InlineData("mercado")]
    public void Text_without_an_amount_is_not_a_compact_entry(string? input)
    {
        Parse(input).Outcome.Should().Be(CompactExpenseOutcome.Unparsed);
    }

    [Fact]
    public void Two_equally_plausible_amounts_are_reported_instead_of_guessed()
    {
        // "12 y 15 pan" is genuinely two numbers. Picking one silently would corrupt a report.
        var result = Parse("12 y 15 pan");

        result.Outcome.Should().Be(CompactExpenseOutcome.Ambiguous);
        result.Amount.Should().BeNull();
        result.Candidates.Should().BeEquivalentTo([12L, 15L]);
    }

    [Fact]
    public void A_clearly_dominant_amount_wins_over_a_count()
    {
        // "2 x 3500" is an amount plus a multiplier, not two competing amounts.
        var result = Parse("2 x 3500 almuerzo");

        result.Outcome.Should().Be(CompactExpenseOutcome.Parsed);
        result.Amount.Should().Be(3_500);
    }

    [Fact]
    public void A_marked_amount_wins_over_a_bare_number()
    {
        var result = Parse("2 x 35.000 almuerzo");

        result.Outcome.Should().Be(CompactExpenseOutcome.Parsed);
        result.Amount.Should().Be(35_000);
    }

    [Fact]
    public void An_invalid_amount_makes_the_message_unparsed_rather_than_silently_ignored()
    {
        // "3,5" is not a valid peso amount, so there is no amount to extract.
        Parse("3,5 pan").Outcome.Should().Be(CompactExpenseOutcome.Unparsed);
    }

    [Fact]
    public void A_very_long_description_is_truncated_to_what_can_be_stored()
    {
        var longDescription = new string('a', 600);

        var result = Parse($"35.000 {longDescription}");

        result.Outcome.Should().Be(CompactExpenseOutcome.Parsed);
        result.Description!.Length.Should().Be(MyBudget.Domain.Expenses.Expense.MaxDescriptionLength);
    }

    [Fact]
    public void A_description_is_trimmed_of_trailing_punctuation()
    {
        Parse("35.000 verduras.").Description.Should().Be("verduras");
    }
}
