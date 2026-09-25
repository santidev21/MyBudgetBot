using System.Globalization;

namespace MyBudget.Application.Money;

internal enum SeparatorOutcome
{
    Resolved,
    Ambiguous,
    Failed,
}

/// <summary>The outcome of interpreting separators in a numeric string.</summary>
internal sealed class SeparatorResolution
{
    private SeparatorResolution(
        SeparatorOutcome outcome,
        decimal value,
        MoneyInputForm form,
        decimal alternative,
        MoneyParseError error)
    {
        Outcome = outcome;
        Value = value;
        Form = form;
        Alternative = alternative;
        Error = error;
    }

    public SeparatorOutcome Outcome { get; }

    public decimal Value { get; }

    public MoneyInputForm Form { get; }

    /// <summary>The other defensible reading, when <see cref="Outcome"/> is ambiguous.</summary>
    public decimal Alternative { get; }

    public MoneyParseError Error { get; }

    public static SeparatorResolution Resolved(decimal value, MoneyInputForm form) =>
        new(SeparatorOutcome.Resolved, value, form, 0m, default);

    public static SeparatorResolution Ambiguous(decimal grouping, decimal fraction) =>
        new(SeparatorOutcome.Ambiguous, grouping, MoneyInputForm.StandardGrouping, fraction, default);

    public static SeparatorResolution Failed(MoneyParseError error) =>
        new(SeparatorOutcome.Failed, 0m, default, 0m, error);
}

/// <summary>
/// Stage 3 of money parsing, and the only place where <c>.</c> and <c>,</c> are interpreted.
/// <para>
/// Input is already lowercased. Leading zeros, extra spaces and stray punctuation are not
/// handled here because <see cref="MoneyInputNormalizer"/> has already dealt with them.
/// Internal arithmetic uses <see cref="decimal"/>, never a binary floating point type.
/// </para>
/// </summary>
internal static class SeparatorResolver
{
    /// <summary>
    /// Beyond this many digits the value cannot be a real amount, and would risk
    /// overflowing <see cref="decimal"/>. The largest accepted amount has 12 digits.
    /// </summary>
    private const int MaxDigits = 18;

    private static readonly char[] AllSeparators = ['.', ',', ' '];

    public static SeparatorResolution Resolve(string numericPart, CurrencyDefinition currency)
    {
        if (string.IsNullOrEmpty(numericPart))
        {
            return SeparatorResolution.Failed(MoneyParseError.NotANumber);
        }

        foreach (var character in numericPart)
        {
            if (!char.IsAsciiDigit(character) && character != '.' && character != ',' && character != ' ')
            {
                return SeparatorResolution.Failed(MoneyParseError.NotANumber);
            }
        }

        var dots = Count(numericPart, '.');
        var commas = Count(numericPart, ',');

        char? decimalSeparator = null;

        if (dots > 0 && commas > 0)
        {
            // Both kinds are present, so the one appearing last is the decimal separator.
            var candidate = numericPart.LastIndexOf('.') > numericPart.LastIndexOf(',') ? '.' : ',';
            if (Count(numericPart, candidate) > 1)
            {
                return SeparatorResolution.Failed(MoneyParseError.MalformedGrouping);
            }

            decimalSeparator = candidate;
        }
        else if (dots == 1 || commas == 1)
        {
            var separator = dots == 1 ? '.' : ',';
            var digitsAfter = numericPart.Length - numericPart.IndexOf(separator) - 1;

            if (digitsAfter == 3)
            {
                // Three digits after a lone separator. COP has no decimal places, so this
                // is always a thousands group and "3.500" is never interrupted. A currency
                // with decimal places genuinely admits both readings.
                if (currency.DecimalPlaces > 0)
                {
                    return ResolveThreeDigitAmbiguity(numericPart, separator, currency);
                }

                decimalSeparator = null;
            }
            else if (digitsAfter is 1 or 2)
            {
                decimalSeparator = separator;
            }
            else
            {
                return SeparatorResolution.Failed(MoneyParseError.MalformedGrouping);
            }
        }

        // Two or more occurrences of a single kind are all grouping separators.

        return decimalSeparator is null
            ? BuildGrouped(numericPart, currency)
            : BuildWithFraction(numericPart, decimalSeparator.Value, currency);
    }

    private static SeparatorResolution ResolveThreeDigitAmbiguity(
        string numericPart, char separator, CurrencyDefinition currency)
    {
        var grouped = BuildGrouped(numericPart, currency);
        var fractional = BuildWithFraction(numericPart, separator, currency);

        var groupedOk = grouped.Outcome == SeparatorOutcome.Resolved;
        var fractionalOk = fractional.Outcome == SeparatorOutcome.Resolved;

        return (groupedOk, fractionalOk) switch
        {
            (true, true) => SeparatorResolution.Ambiguous(grouped.Value, fractional.Value),
            (true, false) => grouped,
            (false, true) => fractional,
            _ => SeparatorResolution.Failed(MoneyParseError.MalformedGrouping),
        };
    }

    private static SeparatorResolution BuildGrouped(string numericPart, CurrencyDefinition currency)
    {
        var groups = numericPart.Split(AllSeparators, StringSplitOptions.None);

        if (!AreValidGroups(groups))
        {
            return SeparatorResolution.Failed(MoneyParseError.MalformedGrouping);
        }

        var digits = string.Concat(groups);
        if (digits.Length > MaxDigits)
        {
            return SeparatorResolution.Failed(MoneyParseError.TooLarge);
        }

        var value = decimal.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return SeparatorResolution.Resolved(value, FormFor(numericPart, currency));
    }

    private static SeparatorResolution BuildWithFraction(
        string numericPart, char decimalSeparator, CurrencyDefinition currency)
    {
        var index = numericPart.LastIndexOf(decimalSeparator);
        var integerPart = numericPart[..index];
        var fractionPart = numericPart[(index + 1)..];

        if (fractionPart.Length == 0)
        {
            return SeparatorResolution.Failed(MoneyParseError.MalformedGrouping);
        }

        var integerGroups = integerPart.Split(AllSeparators, StringSplitOptions.None);
        if (!AreValidGroups(integerGroups))
        {
            return SeparatorResolution.Failed(MoneyParseError.MalformedGrouping);
        }

        var integerDigits = string.Concat(integerGroups);
        if (integerDigits.Length + fractionPart.Length > MaxDigits)
        {
            return SeparatorResolution.Failed(MoneyParseError.TooLarge);
        }

        var text = $"{integerDigits}.{fractionPart}";
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return SeparatorResolution.Failed(MoneyParseError.NotANumber);
        }

        return SeparatorResolution.Resolved(value, FormFor(integerPart, currency));
    }

    /// <summary>
    /// Thousands grouping: the leading group has one to three digits, every following group
    /// has exactly three. A single group is unconstrained, so plain digits are accepted.
    /// </summary>
    private static bool AreValidGroups(string[] groups)
    {
        if (groups.Length == 1)
        {
            return groups[0].Length > 0;
        }

        if (groups[0].Length is 0 or > 3)
        {
            return false;
        }

        for (var index = 1; index < groups.Length; index++)
        {
            if (groups[index].Length != 3)
            {
                return false;
            }
        }

        return true;
    }

    private static MoneyInputForm FormFor(string numericPart, CurrencyDefinition currency)
    {
        if (numericPart.Contains(' ', StringComparison.Ordinal))
        {
            return MoneyInputForm.SpaceGrouped;
        }

        if (numericPart.Contains(currency.GroupSeparator, StringComparison.Ordinal))
        {
            return MoneyInputForm.StandardGrouping;
        }

        return numericPart.IndexOfAny(['.', ',']) >= 0
            ? MoneyInputForm.ToleratedGrouping
            : MoneyInputForm.Plain;
    }

    private static int Count(string text, char value)
    {
        var count = 0;
        foreach (var character in text)
        {
            if (character == value)
            {
                count++;
            }
        }

        return count;
    }
}
