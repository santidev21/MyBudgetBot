namespace MyBudget.Application.Money;

/// <summary>
/// Stage 2 of money parsing: detect and remove a magnitude suffix, for example
/// <c>35k</c>, <c>35 mil</c>, <c>1,5 millones</c>.
/// <para>
/// Runs before separator resolution so a fractional value such as <c>1,5 millones</c> is
/// resolved as a fraction and then scaled, rather than being mistaken for grouping.
/// </para>
/// </summary>
internal static class MagnitudeSuffixResolver
{
    public static bool TryResolve(
        string normalized,
        CurrencyDefinition currency,
        out string numericPart,
        out long multiplier,
        out MoneyParseError error)
    {
        numericPart = normalized;
        multiplier = 1;
        error = MoneyParseError.NotANumber;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        // Longest token first so "millones" wins over "mil".
        foreach (var suffix in currency.MagnitudeSuffixes.OrderByDescending(s => s.Token.Length))
        {
            if (normalized.Length <= suffix.Token.Length)
            {
                continue;
            }

            if (!normalized.EndsWith(suffix.Token, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = normalized[..^suffix.Token.Length].TrimEnd();

            // A suffix with nothing in front of it ("mil") is not an amount.
            if (remainder.Length == 0)
            {
                return false;
            }

            numericPart = remainder;
            multiplier = suffix.Multiplier;
            return true;
        }

        return true;
    }
}
