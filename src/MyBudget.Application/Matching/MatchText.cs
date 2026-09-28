using System.Text;
using MyBudget.Application.Text;

namespace MyBudget.Application.Matching;

/// <summary>
/// Normalisation used only while matching, never for storage or display (design §9).
/// <para>
/// NFKC, accents stripped, lowercase, punctuation turned into spaces, whitespace collapsed and
/// leading articles and prepositions dropped, so "Pagué en el Mercado." and "mercado" compare as
/// the same thing. It reuses <see cref="TextNormalizer"/> for the accent folding so storage and
/// matching cannot drift apart. Singular/plural is handled at token comparison time by
/// <see cref="TokensMatch"/>, because Spanish cannot be stemmed safely without a dictionary.
/// </para>
/// </summary>
internal static class MatchText
{
    private static readonly HashSet<string> LeadingStopWords = new(StringComparer.Ordinal)
    {
        "el", "la", "los", "las", "un", "una", "unos", "unas", "lo",
        "de", "del", "al", "a", "en", "con", "para", "por", "sin",
        "mi", "mis", "tu", "tus", "su", "sus", "nuestro", "nuestra",
    };

    /// <summary>Tokens shorter than this are never plural folded; one edit would dominate.</summary>
    private const int MinimumPluralTokenLength = 4;

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var folded = TextNormalizer.Fold(value.Normalize(NormalizationForm.FormKC));
        var tokens = TokenizeWithPunctuation(folded);

        var start = 0;
        while (start < tokens.Count && LeadingStopWords.Contains(tokens[start]))
        {
            start++;
        }

        return string.Join(' ', tokens.Skip(start));
    }

    public static string[] Tokenize(string normalized) =>
        normalized.Length == 0 ? [] : normalized.Split(' ');

    /// <summary>
    /// Whether two tokens are the same word, allowing the regular Spanish plural forms:
    /// <c>verdura</c>/<c>verduras</c> and <c>flor</c>/<c>flores</c>. The shorter form must have at
    /// least four characters, exactly as the design specifies. Folding is symmetric, so the
    /// comparison never depends on which side happens to be singular.
    /// </summary>
    public static bool TokensMatch(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        return IsPlural(left, right) || IsPlural(right, left);
    }

    private static bool IsPlural(string singular, string plural) =>
        singular.Length >= MinimumPluralTokenLength
        && (string.Equals(plural, singular + "s", StringComparison.Ordinal)
            || string.Equals(plural, singular + "es", StringComparison.Ordinal));

    private static List<string> TokenizeWithPunctuation(string folded)
    {
        var builder = new StringBuilder(folded.Length);

        foreach (var character in folded)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        var tokens = new List<string>();

        foreach (var token in builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            tokens.Add(token);
        }

        return tokens;
    }
}
