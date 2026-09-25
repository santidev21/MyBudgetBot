using MyBudget.Domain.Common;

namespace MyBudget.Domain.Categories;

/// <summary>
/// A spending category owned by a user.
/// Categories are never hard deleted: <see cref="IsActive"/> hides them from pickers and
/// from new monthly allocations while every historical expense keeps its reference.
/// </summary>
public sealed class BudgetCategory : Entity
{
    public const string DefaultIcon = "📦";
    public const int MaxNameLength = 60;
    public const int MaxIconLength = 16;
    public const int MaxAliasLength = 60;

    private readonly List<CategoryAlias> _aliases = [];

    private BudgetCategory()
    {
    }

    public BudgetCategory(Guid userId, string name, string? icon = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        UserId = userId;
        Rename(name);
        ChangeIcon(icon);
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = null!;

    public string Icon { get; private set; } = DefaultIcon;

    public bool IsActive { get; private set; } = true;

    public IReadOnlyList<CategoryAlias> Aliases => _aliases;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException(
                $"Category name cannot exceed {MaxNameLength} characters.", nameof(name));
        }

        Name = trimmed;
    }

    public void ChangeIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            Icon = DefaultIcon;
            return;
        }

        var trimmed = icon.Trim();
        if (trimmed.Length > MaxIconLength)
        {
            throw new ArgumentException(
                $"Icon cannot exceed {MaxIconLength} characters.", nameof(icon));
        }

        Icon = trimmed;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Adds a keyword used by the deterministic category matcher.
    /// <paramref name="normalizedAlias"/> must already be normalized by the caller
    /// (lowercase, accents stripped, whitespace collapsed) so matching is exact.
    /// </summary>
    public CategoryAlias AddAlias(string alias, string normalizedAlias)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("Alias is required.", nameof(alias));
        }

        var trimmedAlias = alias.Trim();
        if (trimmedAlias.Length > MaxAliasLength)
        {
            throw new ArgumentException(
                $"Alias cannot exceed {MaxAliasLength} characters.", nameof(alias));
        }

        if (string.IsNullOrWhiteSpace(normalizedAlias))
        {
            throw new ArgumentException("Normalized alias is required.", nameof(normalizedAlias));
        }

        var normalized = normalizedAlias.Trim();
        if (_aliases.Any(a => a.NormalizedAlias == normalized))
        {
            throw new InvalidOperationException(
                $"Alias '{normalized}' already exists for this category.");
        }

        var created = new CategoryAlias(UserId, Id, trimmedAlias, normalized);
        _aliases.Add(created);
        return created;
    }

    /// <summary>
    /// Finds an alias by its persisted identifier, for callers that hold an id from the
    /// database (for example a Telegram callback). Returns <c>null</c> for
    /// <see cref="Guid.Empty"/>: an alias created in memory has no identifier until the
    /// persistence layer assigns one, so an empty id must never match.
    /// </summary>
    public CategoryAlias? FindAlias(Guid aliasId) =>
        aliasId == Guid.Empty
            ? null
            : _aliases.FirstOrDefault(alias => alias.Id == aliasId);

    /// <summary>
    /// Removes the given alias. Takes the instance rather than an id on purpose: alias
    /// identifiers are assigned on insert, so identifying an alias by id would silently do
    /// nothing for an alias that has not been saved yet.
    /// </summary>
    public bool RemoveAlias(CategoryAlias alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return _aliases.Remove(alias);
    }
}
