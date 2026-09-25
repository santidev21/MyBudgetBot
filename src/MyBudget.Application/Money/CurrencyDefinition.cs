namespace MyBudget.Application.Money;

/// <summary>
/// A spoken or shorthand multiplier, for example <c>k</c>, <c>mil</c> or <c>millones</c>.
/// </summary>
public sealed record MagnitudeSuffix(string Token, long Multiplier);

/// <summary>
/// Everything the money parser and formatter need to know about a currency.
/// <para>
/// This is data on purpose: adding a currency means adding an entry, not changing the
/// parser. The parser's only currency-specific behaviour is driven by
/// <see cref="DecimalPlaces"/> and the separator characters below.
/// </para>
/// </summary>
public sealed record CurrencyDefinition
{
    public required string Code { get; init; }

    public required string Symbol { get; init; }

    /// <summary>
    /// Digits after the decimal separator. Cop is zero: 35.000 COP is 35000 minor units.
    /// This also drives whether a separator followed by three digits may be a fraction.
    /// </summary>
    public int DecimalPlaces { get; init; }

    public string GroupSeparator { get; init; } = ".";

    public string DecimalSeparator { get; init; } = ",";

    public IReadOnlyList<MagnitudeSuffix> MagnitudeSuffixes { get; init; } = [];

    /// <summary>Words that may appear in the input, for example <c>pesos</c>.</summary>
    public IReadOnlyList<string> SpokenNames { get; init; } = [];

    /// <summary>
    /// A bare <c>m</c> is deliberately not a suffix: "35 m" would otherwise silently become
    /// 35 million. Users who mean millions type "mm", "millon" or "millones".
    /// </summary>
    public static CurrencyDefinition ColombianPeso { get; } = new()
    {
        Code = "COP",
        Symbol = "$",
        DecimalPlaces = 0,
        GroupSeparator = ".",
        DecimalSeparator = ",",
        MagnitudeSuffixes =
        [
            new MagnitudeSuffix("k", 1_000),
            new MagnitudeSuffix("mil", 1_000),
            new MagnitudeSuffix("mm", 1_000_000),
            new MagnitudeSuffix("millon", 1_000_000),
            new MagnitudeSuffix("millón", 1_000_000),
            new MagnitudeSuffix("millones", 1_000_000),
        ],
        SpokenNames = ["peso", "pesos", "colombiano", "colombianos", "cop"],
    };

    /// <summary>How many minor units make one whole unit (1 for COP, 100 for a 2-decimal currency).</summary>
    public long MinorUnitsFactor
    {
        get
        {
            var factor = 1L;
            for (var i = 0; i < DecimalPlaces; i++)
            {
                factor *= 10;
            }

            return factor;
        }
    }
}
