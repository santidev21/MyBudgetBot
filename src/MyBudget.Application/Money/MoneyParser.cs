using MyBudget.Domain.Common;

namespace MyBudget.Application.Money;

public interface IMoneyParser
{
    MoneyParseResult Parse(string? input, string currencyCode);

    MoneyParseResult Parse(string? input, CurrencyDefinition currency);

    bool TryParse(string? input, string currencyCode, out long amount);
}

/// <summary>
/// Turns what a user typed into an exact amount.
/// <para>
/// Never throws on user input, and never silently produces a dangerous interpretation:
/// every outcome is either a value, an explicit request for clarification, or a reason the
/// input was rejected.
/// </para>
/// </summary>
public sealed class MoneyParser(ICurrencyRegistry currencies, IMoneyFormatter formatter) : IMoneyParser
{
    public MoneyParseResult Parse(string? input, string currencyCode) =>
        Parse(input, currencies.Get(currencyCode));

    public MoneyParseResult Parse(string? input, CurrencyDefinition currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        var raw = input ?? string.Empty;

        if (!MoneyInputNormalizer.TryNormalize(raw, currency, out var normalized, out var normalizeError))
        {
            return new MoneyParseResult.Invalid(raw, normalizeError);
        }

        if (!MagnitudeSuffixResolver.TryResolve(
                normalized, currency, out var numericPart, out var multiplier, out var suffixError))
        {
            return new MoneyParseResult.Invalid(raw, suffixError);
        }

        var resolution = SeparatorResolver.Resolve(numericPart, currency);
        var form = multiplier == 1 ? resolution.Form : MoneyInputForm.SuffixScaled;

        return resolution.Outcome switch
        {
            SeparatorOutcome.Failed => new MoneyParseResult.Invalid(raw, resolution.Error),

            SeparatorOutcome.Resolved => TryScale(resolution.Value, multiplier, currency, out var amount, out var error)
                ? Succeed(raw, amount, currency, form)
                : new MoneyParseResult.Invalid(raw, error),

            _ => ResolveAmbiguity(raw, resolution, multiplier, currency, form),
        };
    }

    public bool TryParse(string? input, string currencyCode, out long amount)
    {
        var result = Parse(input, currencyCode);
        amount = result is MoneyParseResult.Success success ? success.Amount : 0;
        return result is MoneyParseResult.Success;
    }

    private MoneyParseResult ResolveAmbiguity(
        string raw,
        SeparatorResolution resolution,
        long multiplier,
        CurrencyDefinition currency,
        MoneyInputForm form)
    {
        var candidates = new List<long>(2);

        if (TryScale(resolution.Value, multiplier, currency, out var grouped, out _))
        {
            candidates.Add(grouped);
        }

        if (TryScale(resolution.Alternative, multiplier, currency, out var fractional, out _))
        {
            candidates.Add(fractional);
        }

        var distinct = candidates.Distinct().ToList();

        return distinct.Count switch
        {
            0 => new MoneyParseResult.Invalid(raw, resolution.Error == default
                ? MoneyParseError.MalformedGrouping
                : resolution.Error),
            1 => Succeed(raw, distinct[0], currency, form),
            _ => new MoneyParseResult.Ambiguous(raw, distinct, MoneyAmbiguityReason.SeparatorRoleUnclear),
        };
    }

    private MoneyParseResult Succeed(string raw, long amount, CurrencyDefinition currency, MoneyInputForm form) =>
        new MoneyParseResult.Success(amount, formatter.Format(amount, currency), form);

    /// <summary>
    /// Applies the magnitude multiplier and converts to minor units, requiring the result to
    /// be a whole number of minor units. There is no rounding: a value with a fraction the
    /// currency cannot express is rejected rather than quietly changed.
    /// </summary>
    private static bool TryScale(
        decimal value,
        long multiplier,
        CurrencyDefinition currency,
        out long amount,
        out MoneyParseError error)
    {
        amount = 0;
        error = MoneyParseError.NonPositive;

        var total = value * multiplier;

        if (total <= 0m)
        {
            return false;
        }

        var scaled = total * currency.MinorUnitsFactor;

        if (scaled != decimal.Truncate(scaled))
        {
            error = MoneyParseError.FractionNotAllowed;
            return false;
        }

        if (scaled > MoneyLimits.MaxAmount)
        {
            error = MoneyParseError.TooLarge;
            return false;
        }

        amount = (long)scaled;
        return true;
    }
}
