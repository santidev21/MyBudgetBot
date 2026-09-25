using FluentAssertions;
using MyBudget.Application.Money;

namespace MyBudget.Application.Tests.Money;

public sealed class CurrencyRegistryTests
{
    [Fact]
    public void Colombian_peso_is_registered_by_default()
    {
        var registry = new CurrencyRegistry();

        registry.All.Should().ContainSingle();
        registry.Get("COP").Code.Should().Be("COP");
        registry.Get("COP").DecimalPlaces.Should().Be(0);
        registry.Get("COP").Symbol.Should().Be("$");
    }

    [Theory]
    [InlineData("cop")]
    [InlineData("Cop")]
    [InlineData(" COP ")]
    public void Lookup_ignores_case_and_surrounding_space(string code)
    {
        new CurrencyRegistry().Get(code).Code.Should().Be("COP");
    }

    [Fact]
    public void An_unknown_code_is_reported_rather_than_silently_defaulted()
    {
        var registry = new CurrencyRegistry();

        registry.TryGet("XYZ", out _).Should().BeFalse();
        registry.Invoking(r => r.Get("XYZ")).Should().Throw<UnsupportedCurrencyException>();
    }

    [Fact]
    public void Registering_a_currency_does_not_require_touching_the_parser()
    {
        var registry = new CurrencyRegistry([CurrencyDefinition.ColombianPeso, TestServices.TwoDecimalCurrency]);

        registry.All.Should().HaveCount(2);
        registry.Get("EUR").DecimalPlaces.Should().Be(2);
        registry.Get("EUR").MinorUnitsFactor.Should().Be(100);
    }

    [Fact]
    public void A_registry_without_currencies_is_rejected()
    {
        var act = () => new CurrencyRegistry([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_bare_m_is_not_a_million_suffix()
    {
        // "35 m" must never silently become 35 million.
        CurrencyDefinition.ColombianPeso.MagnitudeSuffixes
            .Should().NotContain(suffix => suffix.Token == "m");

        CurrencyDefinition.ColombianPeso.MagnitudeSuffixes
            .Should().Contain(suffix => suffix.Token == "mm" && suffix.Multiplier == 1_000_000);
    }
}
