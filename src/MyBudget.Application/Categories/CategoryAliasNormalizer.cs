using MyBudget.Application.Text;

namespace MyBudget.Application.Categories;

/// <summary>
/// Produces the stored form of an alias: lowercase, accent-free and whitespace-collapsed.
/// <para>
/// Both <c>CategoryAlias.NormalizedAlias</c> and the per-category unique constraint use this
/// form, so "Café" and "cafe" are the same keyword. Storage and lookup must agree exactly;
/// that is the same rule the category name lookup already follows.
/// </para>
/// </summary>
internal static class CategoryAliasNormalizer
{
    public static string Normalize(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return string.Empty;
        }

        var folded = TextNormalizer.Fold(alias);

        // Fold already lowercases; this collapses any run of whitespace to a single space and
        // drops leading and trailing whitespace.
        return string.Join(' ', folded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
