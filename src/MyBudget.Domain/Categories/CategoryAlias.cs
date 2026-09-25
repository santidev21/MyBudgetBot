using MyBudget.Domain.Common;

namespace MyBudget.Domain.Categories;

/// <summary>
/// A word or phrase associated with a category, used by the deterministic category matcher.
/// The same normalized alias may exist on two different categories on purpose: that is how
/// genuine ambiguity ("comida" = restaurants or groceries) is modelled, and the bot asks
/// the user instead of guessing.
/// </summary>
public sealed class CategoryAlias : Entity
{
    private CategoryAlias()
        : base(keyGeneratedByStore: true)
    {
    }

    internal CategoryAlias(Guid userId, Guid categoryId, string alias, string normalizedAlias)
        : base(keyGeneratedByStore: true)
    {
        UserId = userId;
        CategoryId = categoryId;
        Alias = alias;
        NormalizedAlias = normalizedAlias;
    }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

    /// <summary>Display form, exactly as the user typed it.</summary>
    public string Alias { get; private set; } = null!;

    /// <summary>Lowercase, accent-free, whitespace-collapsed form used for matching.</summary>
    public string NormalizedAlias { get; private set; } = null!;
}
