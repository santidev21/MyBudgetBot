using FluentAssertions;
using Microsoft.Extensions.Options;
using MyBudget.Application.Matching;

namespace MyBudget.Application.Tests.Matching;

/// <summary>
/// The deterministic matcher corpus (design §9).
/// <para>
/// Every signal, both thresholds and the fuzzy default are pinned here. The corpus is the contract:
/// the expense flow trusts these outcomes, so a change in scoring has to be a deliberate, visible
/// change to a test.
/// </para>
/// </summary>
public sealed class CategoryMatcherTests
{
    private static readonly CategoryMatcher Default = new(
        Microsoft.Extensions.Options.Options.Create(new CategoryMatchingOptions()));

    private static readonly CategoryMatcher Fuzzy = new(
        Microsoft.Extensions.Options.Options.Create(new CategoryMatchingOptions { EnableFuzzy = true }));

    private static CategoryMatchInput Category(string name, params string[] aliases) =>
        new(Guid.NewGuid(), name, "🔖", aliases);

    [Fact]
    public void An_exact_keyword_match_is_matched()
    {
        var mercado = Category("Mercado", "verduras");

        var result = Default.Match("verduras", [mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
        result.Suggestion!.CategoryId.Should().Be(mercado.CategoryId);
    }

    [Fact]
    public void An_exact_category_name_match_is_matched()
    {
        var mercado = Category("Mercado");

        var result = Default.Match("Mercado", [mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
        result.Suggestion!.CategoryId.Should().Be(mercado.CategoryId);
    }

    [Theory]
    [InlineData("CAFÉ")]
    [InlineData("cafe")]
    [InlineData("  Café. ")]
    public void Folding_ignores_case_accents_and_punctuation(string query)
    {
        var cafe = Category("Café");

        var result = Default.Match(query, [cafe]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
    }

    [Theory]
    [InlineData("mercado")]
    [InlineData("en el mercado")]
    [InlineData("del mercado")]
    public void Leading_articles_and_prepositions_are_stripped(string query)
    {
        var mercado = Category("Mercado");

        var result = Default.Match(query, [mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
    }

    [Theory]
    [InlineData("verdura")]
    [InlineData("verduras")]
    public void Singular_and_plural_match_each_other(string query)
    {
        var mercado = Category("Mercado", "verduras");

        var result = Default.Match(query, [mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
    }

    [Fact]
    public void An_alias_within_a_longer_description_is_a_phrase_match()
    {
        var mercado = Category("Mercado");

        var result = Default.Match("mercado de verduras", [mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
        result.Suggestion!.CategoryId.Should().Be(mercado.CategoryId);
    }

    [Fact]
    public void Full_token_coverage_of_two_or_more_tokens_is_matched()
    {
        var restaurantes = Category("Restaurantes", "arroz con pollo");

        var result = Default.Match("pollo arroz", [restaurantes]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
    }

    [Fact]
    public void Two_categories_sharing_a_keyword_are_ambiguous_and_never_guessed()
    {
        var restaurantes = Category("Restaurantes", "comida");
        var mercado = Category("Mercado", "comida");

        var result = Default.Match("comida", [restaurantes, mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Ambiguous);
        result.Suggestion.Should().BeNull();
        result.Candidates.Select(candidate => candidate.CategoryId)
            .Should().BeEquivalentTo([restaurantes.CategoryId, mercado.CategoryId]);
    }

    [Fact]
    public void A_close_second_candidate_within_the_margin_is_ambiguous()
    {
        var mercado = Category("Mercado");                       // exact name: 1.00
        var plaza = Category("Plaza", "mercado de verduras");    // phrase: ~0.87

        var result = Default.Match("mercado", [mercado, plaza]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Ambiguous);
    }

    [Fact]
    public void A_clear_winner_is_matched_even_with_other_categories_present()
    {
        var mercado = Category("Mercado", "verduras");
        var transporte = Category("Transporte", "bus");

        var result = Default.Match("verduras", [mercado, transporte]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
        result.Suggestion!.CategoryId.Should().Be(mercado.CategoryId);
    }

    [Fact]
    public void Nothing_recognizable_is_none()
    {
        var mercado = Category("Mercado");

        var result = Default.Match("xyz", [mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.None);
        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_query_matches_nothing()
    {
        Default.Match("   ", [Category("Mercado")]).Outcome.Should().Be(CategoryMatchOutcome.None);
    }

    [Fact]
    public void A_typo_is_none_while_fuzzy_is_off_by_default()
    {
        var mercado = Category("Mercado", "mercado");

        // "mercad" is one edit away from "mercado" but fuzzy ships off: asking beats guessing.
        Default.Match("mercad", [mercado]).Outcome.Should().Be(CategoryMatchOutcome.None);
    }

    [Fact]
    public void A_typo_is_matched_when_fuzzy_is_enabled()
    {
        var mercado = Category("Zzz", "mercado");

        var result = Fuzzy.Match("mercad", [mercado]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Matched);
        result.Suggestion!.CategoryId.Should().Be(mercado.CategoryId);
    }

    [Fact]
    public void Two_equally_close_typos_are_ambiguous_even_with_fuzzy_on()
    {
        var mercado = Category("Mercado");
        var mercada = Category("Mercada");

        var result = Fuzzy.Match("mercad", [mercado, mercada]);

        result.Outcome.Should().Be(CategoryMatchOutcome.Ambiguous);
    }

    [Fact]
    public void Fuzzy_is_restricted_to_a_single_token()
    {
        var mercado = Category("Mercado", "mercado");

        // Two tokens exceed the default FuzzyMaxTokens of one, so fuzzy does not fire.
        Fuzzy.Match("mercad grande", [mercado]).Outcome.Should().Be(CategoryMatchOutcome.None);
    }

    [Fact]
    public void Fuzzy_ignores_tokens_shorter_than_the_configured_minimum()
    {
        var cafe = Category("Cafe", "cafe");

        Fuzzy.Match("caf", [cafe]).Outcome.Should().Be(CategoryMatchOutcome.None);
    }

    [Fact]
    public void The_category_name_earns_a_small_bonus_over_an_alias()
    {
        var named = Category("arroz con pollo");
        var aliased = Category("Sin relación", "arroz con pollo");

        // Both reach full coverage at 0.80; only the name term is boosted to 0.85.
        Default.Score("arroz pollo", named).Should().BeApproximately(0.85, 1e-9);
        Default.Score("arroz pollo", aliased).Should().BeApproximately(0.80, 1e-9);
    }

    [Fact]
    public void A_single_fuzzy_alias_reaches_the_selection_threshold()
    {
        var mercado = Category("Zzz", "mercado");

        // 0.20 partial-overlap floor + 0.45 fuzzy = 0.65, exactly the threshold.
        Fuzzy.Score("mercad", mercado).Should().BeApproximately(0.65, 1e-9);
    }
}
