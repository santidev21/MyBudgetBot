using System.Globalization;
using System.Text;

namespace MyBudget.Application.Text;

/// <summary>
/// Shared text handling for input parsing.
/// <para>
/// Accent folding matters in Spanish: users write "pagué" and "pague", "café" and "cafe",
/// and a matcher that treats those as different words is simply wrong. The category matcher
/// builds on this later.
/// </para>
/// </summary>
internal static class TextNormalizer
{
    /// <summary>Lowercases and strips diacritics, for example <c>Pagué</c> becomes <c>pague</c>.</summary>
    public static string Fold(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
