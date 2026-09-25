using System.Globalization;
using System.Text;

namespace MyBudget.Application.Money;

public interface IMoneyFormatter
{
    string Format(long amount, string currencyCode);

    string Format(long amount, CurrencyDefinition currency);

    /// <summary>Formats a percentage with one decimal place and a comma, for example <c>60,6 %</c>.</summary>
    string FormatPercentage(decimal percentage);
}

/// <summary>
/// Renders amounts for the user.
/// <para>
/// Built on explicit separators rather than on <c>CultureInfo</c> output: ICU data changes
/// between machines and image versions, and a currency format that drifts with the host is
/// not acceptable in a ledger. Only whole minor units are formatted, never a formatted
/// string stored back.
/// </para>
/// </summary>
public sealed class MoneyFormatter(ICurrencyRegistry currencies) : IMoneyFormatter
{
    public string Format(long amount, string currencyCode) =>
        Format(amount, currencies.Get(currencyCode));

    public string Format(long amount, CurrencyDefinition currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        // decimal keeps this exact and avoids the long.MinValue overflow of Math.Abs.
        var value = (decimal)amount;
        var isNegative = value < 0;
        value = Math.Abs(value);

        var factor = currency.MinorUnitsFactor;
        var whole = (long)(value / factor);
        var fraction = (long)(value - (whole * factor));

        var builder = new StringBuilder();
        if (isNegative)
        {
            builder.Append('-');
        }

        builder.Append(currency.Symbol);
        builder.Append(Group(whole, currency.GroupSeparator));

        if (currency.DecimalPlaces > 0)
        {
            builder.Append(currency.DecimalSeparator);
            builder.Append(fraction
                .ToString(CultureInfo.InvariantCulture)
                .PadLeft(currency.DecimalPlaces, '0'));
        }

        return builder.ToString();
    }

    public string FormatPercentage(decimal percentage)
    {
        var rounded = Math.Round(percentage, 1, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');
        return $"{text} %";
    }

    private static string Group(long value, string groupSeparator)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder(digits.Length + (digits.Length / 3));

        for (var index = 0; index < digits.Length; index++)
        {
            if (index > 0 && (digits.Length - index) % 3 == 0)
            {
                builder.Append(groupSeparator);
            }

            builder.Append(digits[index]);
        }

        return builder.ToString();
    }
}
