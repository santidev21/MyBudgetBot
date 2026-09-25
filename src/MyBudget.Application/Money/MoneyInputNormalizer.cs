using System.Text;

namespace MyBudget.Application.Money;

/// <summary>
/// Stage 1 of money parsing: turn arbitrary user text into a canonical form.
/// <para>
/// Handles Unicode compatibility forms, the several space characters users actually send
/// (including non-breaking and thin spaces from mobile keyboards), the currency symbol and
/// spoken currency words. Magnitude suffixes are left in place; stripping them is the next
/// stage's job, because only it knows they are suffixes.
/// </para>
/// </summary>
internal static class MoneyInputNormalizer
{
    private static readonly char[] TrailingNoise = ['.', ',', ';', ':', '!', '?', ')', '('];

    /// <summary>
    /// Words that only join a number to its currency, as in "1,5 millones de pesos".
    /// Dropping them lets the magnitude suffix sit at the end where it is expected.
    /// </summary>
    private static readonly HashSet<string> Connectors = new(StringComparer.Ordinal) { "de", "del" };

    public static bool TryNormalize(
        string? input,
        CurrencyDefinition currency,
        out string normalized,
        out MoneyParseError error)
    {
        normalized = string.Empty;
        error = MoneyParseError.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = NormalizeCharacters(input);

        if (text.StartsWith('-'))
        {
            // Amounts are magnitudes: a negative expense is a data entry mistake, not a sign.
            error = MoneyParseError.Negative;
            return false;
        }

        text = text.TrimStart('+');
        text = RemoveSymbols(text, currency);
        text = RemoveCurrencyWords(text, currency);
        text = CollapseSpaces(text);
        text = text.TrimEnd(TrailingNoise).Trim();

        if (text.Length == 0)
        {
            return false;
        }

        normalized = text;
        return true;
    }

    private static string NormalizeCharacters(string input)
    {
        var decomposed = input.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            builder.Append(IsSpaceLike(character) ? ' ' : char.ToLowerInvariant(character));
        }

        return builder.ToString().Trim();
    }

    private static bool IsSpaceLike(char character) => character switch
    {
        ' ' or '\t' or '\n' or '\r' => true,
        '\u00A0' or '\u2007' or '\u2009' or '\u202F' => true, // NBSP, figure, thin, narrow NBSP
        _ => false,
    };

    private static string RemoveSymbols(string text, CurrencyDefinition currency)
    {
        // '$' is stripped for every currency: users type it reflexively regardless of the
        // configured symbol.
        return text
            .Replace(currency.Symbol, " ", StringComparison.Ordinal)
            .Replace("$", " ", StringComparison.Ordinal);
    }

    private static string RemoveCurrencyWords(string text, CurrencyDefinition currency)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(tokens.Length);

        foreach (var token in tokens)
        {
            if (IsCurrencyWord(token, currency) || Connectors.Contains(token))
            {
                continue;
            }

            kept.Add(StripAttachedCurrencyWord(token, currency));
        }

        return string.Join(' ', kept);
    }

    private static bool IsCurrencyWord(string token, CurrencyDefinition currency) =>
        string.Equals(token, currency.Code, StringComparison.Ordinal)
        || currency.SpokenNames.Any(name => string.Equals(name, token, StringComparison.Ordinal));

    /// <summary>Handles notes without a space, for example <c>35000pesos</c>.</summary>
    private static string StripAttachedCurrencyWord(string token, CurrencyDefinition currency)
    {
        foreach (var name in currency.SpokenNames.OrderByDescending(name => name.Length))
        {
            if (token.Length > name.Length && token.EndsWith(name, StringComparison.Ordinal))
            {
                return token[..^name.Length];
            }
        }

        return token;
    }

    private static string CollapseSpaces(string text) =>
        string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
