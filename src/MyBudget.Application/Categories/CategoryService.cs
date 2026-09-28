using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Categories;

namespace MyBudget.Application.Categories;

/// <summary>Why a category mutation ended the way it did, in terms the presentation can render.</summary>
public enum CategoryChangeStatus
{
    /// <summary>The category was created or updated.</summary>
    Saved,

    /// <summary>An inactive category already used the name and was reactivated instead.</summary>
    Reactivated,

    /// <summary>An active category already uses that name; the user must pick another.</summary>
    NameTaken,

    /// <summary>The target category does not exist for this user.</summary>
    NotFound,
}

/// <summary>The outcome of a category mutation, plus the affected category when there is one.</summary>
public sealed record CategoryChangeResult(CategoryChangeStatus Status, BudgetCategory? Category = null)
{
    public bool Succeeded => Status is CategoryChangeStatus.Saved or CategoryChangeStatus.Reactivated;

    public static CategoryChangeResult Saved(BudgetCategory category) =>
        new(CategoryChangeStatus.Saved, category);

    public static CategoryChangeResult Reactivated(BudgetCategory category) =>
        new(CategoryChangeStatus.Reactivated, category);

    public static CategoryChangeResult NameTaken() => new(CategoryChangeStatus.NameTaken);

    public static CategoryChangeResult NotFound() => new(CategoryChangeStatus.NotFound);
}

/// <summary>The outcome of trying to add a keyword to a category.</summary>
public enum AliasChangeStatus
{
    Added,

    /// <summary>The term is already a keyword of this same category.</summary>
    Duplicate,

    /// <summary>The term belongs to other categories; the caller may confirm and add it anyway.</summary>
    Conflict,

    NotFound,
}

public sealed record AliasChangeResult(
    AliasChangeStatus Status,
    IReadOnlyList<BudgetCategory> ConflictingCategories)
{
    public static AliasChangeResult Added() => new(AliasChangeStatus.Added, []);

    public static AliasChangeResult Duplicate() => new(AliasChangeStatus.Duplicate, []);

    public static AliasChangeResult Conflict(IReadOnlyList<BudgetCategory> conflicts) =>
        new(AliasChangeStatus.Conflict, conflicts);

    public static AliasChangeResult NotFound() => new(AliasChangeStatus.NotFound, []);
}

/// <summary>
/// Category management: create, rename, change the icon, activate or deactivate, list, and the
/// keyword aliases used by the deterministic matcher.
/// <para>
/// Deletion is deliberately absent. A category with expenses or a funded allocation cannot be
/// deleted, and reactivation is offered instead when a name is reused.
/// </para>
/// </summary>
public interface ICategoryService
{
    Task<IReadOnlyList<BudgetCategory>> ListAsync(
        Guid userId, bool includeInactive, CancellationToken cancellationToken = default);

    /// <summary>
    /// A single category owned by the user, or <c>null</c>. Used by the flows that re-read a
    /// category before rendering it or flipping its state.
    /// </summary>
    Task<BudgetCategory?> GetAsync(
        Guid userId, Guid categoryId, CancellationToken cancellationToken = default);

    Task<CategoryChangeResult> CreateAsync(
        Guid userId, string name, string? icon, CancellationToken cancellationToken = default);

    Task<CategoryChangeResult> RenameAsync(
        Guid userId, Guid categoryId, string name, CancellationToken cancellationToken = default);

    Task<CategoryChangeResult> ChangeIconAsync(
        Guid userId, Guid categoryId, string? icon, CancellationToken cancellationToken = default);

    Task<CategoryChangeResult> SetActiveAsync(
        Guid userId, Guid categoryId, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a keyword. When the term already belongs to another category the result is
    /// <see cref="AliasChangeStatus.Conflict"/> unless <paramref name="allowConflict"/> is set:
    /// the caller shows the conflict, and adding anyway is how a deliberate ambiguity is stored.
    /// </summary>
    Task<AliasChangeResult> AddAliasAsync(
        Guid userId, Guid categoryId, string alias, bool allowConflict,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a keyword by its term rather than by id, because an alias created in memory has
    /// no identifier until it is inserted.
    /// </summary>
    Task<bool> RemoveAliasAsync(
        Guid userId, Guid categoryId, string alias, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class CategoryService(
    ICategoryRepository categories,
    IUnitOfWork unitOfWork) : ICategoryService
{
    public Task<IReadOnlyList<BudgetCategory>> ListAsync(
        Guid userId, bool includeInactive, CancellationToken cancellationToken = default)
        => categories.ListAsync(userId, includeInactive, cancellationToken);

    public Task<BudgetCategory?> GetAsync(
        Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
        => categories.FindByIdAsync(userId, categoryId, cancellationToken);

    public async Task<CategoryChangeResult> CreateAsync(
        Guid userId, string name, string? icon, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var existing = await categories.FindByNameAsync(userId, name, cancellationToken);
        if (existing is not null)
        {
            return existing.IsActive
                ? CategoryChangeResult.NameTaken()
                : await ReactivateAsync(existing, icon, cancellationToken);
        }

        var created = new BudgetCategory(userId, name, icon);
        categories.Add(created);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return CategoryChangeResult.Saved(created);
        }
        catch (UniqueConstraintViolationException)
        {
            // Somebody else inserted the same name between the read and the write. The unique
            // index is the arbiter: detach the loser and resolve against the winner.
            unitOfWork.Detach(created);

            var winner = await categories.FindByNameAsync(userId, name, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return winner.IsActive
                ? CategoryChangeResult.NameTaken()
                : await ReactivateAsync(winner, icon, cancellationToken);
        }
    }

    public async Task<CategoryChangeResult> RenameAsync(
        Guid userId, Guid categoryId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var category = await categories.FindByIdAsync(userId, categoryId, cancellationToken);
        if (category is null)
        {
            return CategoryChangeResult.NotFound();
        }

        var existing = await categories.FindByNameAsync(userId, name, cancellationToken);
        if (existing is not null && existing.Id != categoryId)
        {
            // Renaming onto any existing name is refused, whether that category is active or
            // not: silently reactivating a different category would be a surprising side effect.
            return CategoryChangeResult.NameTaken();
        }

        category.Rename(name);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryChangeResult.Saved(category);
    }

    public async Task<CategoryChangeResult> ChangeIconAsync(
        Guid userId, Guid categoryId, string? icon, CancellationToken cancellationToken = default)
    {
        var category = await categories.FindByIdAsync(userId, categoryId, cancellationToken);
        if (category is null)
        {
            return CategoryChangeResult.NotFound();
        }

        category.ChangeIcon(icon);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryChangeResult.Saved(category);
    }

    public async Task<CategoryChangeResult> SetActiveAsync(
        Guid userId, Guid categoryId, bool isActive, CancellationToken cancellationToken = default)
    {
        var category = await categories.FindByIdAsync(userId, categoryId, cancellationToken);
        if (category is null)
        {
            return CategoryChangeResult.NotFound();
        }

        if (isActive)
        {
            category.Activate();
        }
        else
        {
            category.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryChangeResult.Saved(category);
    }

    public async Task<AliasChangeResult> AddAliasAsync(
        Guid userId, Guid categoryId, string alias, bool allowConflict,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);

        var category = await categories.FindByIdAsync(userId, categoryId, cancellationToken);
        if (category is null)
        {
            return AliasChangeResult.NotFound();
        }

        var normalized = CategoryAliasNormalizer.Normalize(alias);

        if (category.Aliases.Any(existing => existing.NormalizedAlias == normalized))
        {
            return AliasChangeResult.Duplicate();
        }

        var owners = await categories.FindByAliasAsync(userId, normalized, cancellationToken);
        var conflicts = owners.Where(owner => owner.Id != categoryId).ToList();

        if (conflicts.Count > 0 && !allowConflict)
        {
            return AliasChangeResult.Conflict(conflicts);
        }

        category.AddAlias(alias, normalized);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return AliasChangeResult.Added();
    }

    public async Task<bool> RemoveAliasAsync(
        Guid userId, Guid categoryId, string alias, CancellationToken cancellationToken = default)
    {
        var category = await categories.FindByIdAsync(userId, categoryId, cancellationToken);
        if (category is null)
        {
            return false;
        }

        var normalized = CategoryAliasNormalizer.Normalize(alias);
        var match = category.Aliases.FirstOrDefault(existing => existing.NormalizedAlias == normalized);
        if (match is null)
        {
            return false;
        }

        // Removed by instance: an alias created in memory has no id to remove it by.
        category.RemoveAlias(match);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<CategoryChangeResult> ReactivateAsync(
        BudgetCategory category, string? icon, CancellationToken cancellationToken)
    {
        category.Activate();

        // Reusing a name is the user's way of asking for the old category back. Keeping its
        // history and letting the new icon land is more useful than a duplicate.
        if (!string.IsNullOrWhiteSpace(icon))
        {
            category.ChangeIcon(icon);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CategoryChangeResult.Reactivated(category);
    }
}
