using MyBudget.Domain.Categories;

namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// Every method takes <paramref name="userId"/> as its first argument on purpose:
/// ownership is enforced at the call site and a missing scope is a compile error,
/// not a runtime leak.
/// </summary>
public interface ICategoryRepository
{
    Task<IReadOnlyList<BudgetCategory>> ListAsync(
        Guid userId, bool includeInactive, CancellationToken cancellationToken = default);

    Task<BudgetCategory?> FindByIdAsync(
        Guid userId, Guid categoryId, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive lookup by name, used to detect duplicates before creating.</summary>
    Task<BudgetCategory?> FindByNameAsync(
        Guid userId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Categories that already own the given normalized keyword. More than one is legal
    /// (ambiguity is modelled in data), so this returns every owner for the conflict prompt.
    /// </summary>
    Task<IReadOnlyList<BudgetCategory>> FindByAliasAsync(
        Guid userId, string normalizedAlias, CancellationToken cancellationToken = default);

    Task<bool> AnyByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    void Add(BudgetCategory category);
}
