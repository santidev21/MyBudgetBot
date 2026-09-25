using MyBudget.Application.Dates;
using MyBudget.Application.Money;

namespace MyBudget.Application.Tests;

/// <summary>
/// The real service graph for the pure input-handling services, wired exactly as the
/// application registers it. No mocks: these are stateless and deterministic, so testing
/// the real objects is both simpler and more honest.
/// </summary>
internal static class TestServices
{
    public static ICurrencyRegistry Currencies { get; } = new CurrencyRegistry();

    public static IMoneyFormatter Formatter { get; } = new MoneyFormatter(Currencies);

    public static IMoneyParser MoneyParser { get; } = new MoneyParser(Currencies, Formatter);

    public static ICompactExpenseParser CompactParser { get; } = new CompactExpenseParser(Currencies, MoneyParser);

    public static IDateParser DateParser { get; } = new DateParser();

    /// <summary>A currency with decimal places, used to exercise parser behaviour COP cannot reach.</summary>
    public static CurrencyDefinition TwoDecimalCurrency { get; } = new()
    {
        Code = "EUR",
        Symbol = "€",
        DecimalPlaces = 2,
        GroupSeparator = ".",
        DecimalSeparator = ",",
        MagnitudeSuffixes = [new MagnitudeSuffix("k", 1_000)],
        SpokenNames = ["euro", "euros"],
    };
}
