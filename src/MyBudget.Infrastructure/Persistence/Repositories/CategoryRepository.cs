using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Categories;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository(MyBudgetDbContext dbContext) : ICategoryRepository
{
    public async Task<IReadOnlyList<BudgetCategory>> ListAsync(
        Guid userId, bool includeInactive, CancellationToken cancellationToken = default)
    {
        IQueryable<BudgetCategory> query = dbContext.Categories
            .AsNoTracking()
            .Include(category => category.Aliases)
            .Where(category => category.UserId == userId);

        if (!includeInactive)
        {
            query = query.Where(category => category.IsActive);
        }

        return await query
            .OrderBy(category => category.Name)
            .ToListAsync(cancellationToken);
    }

    public Task<BudgetCategory?> FindByIdAsync(
        Guid userId, Guid categoryId, CancellationToken cancellationToken = default)
        => dbContext.Categories
            .Include(category => category.Aliases)
            .FirstOrDefaultAsync(
                category => category.UserId == userId && category.Id == categoryId, cancellationToken);

    public Task<BudgetCategory?> FindByNameAsync(
        Guid userId, string name, CancellationToken cancellationToken = default)
    {
        // Normalised exactly like the unique index uq_categories_user_name, which is
        // (user_id, lower(btrim(name))). Lookup and constraint must agree, otherwise a name
        // the database considers taken could be reported as free.
        var normalized = name.Trim().ToLowerInvariant();

        return dbContext.Categories
            .FirstOrDefaultAsync(
                category => category.UserId == userId
                            && category.Name.Trim().ToLower() == normalized,
                cancellationToken);
    }

    public async Task<IReadOnlyList<BudgetCategory>> FindByAliasAsync(
        Guid userId, string normalizedAlias, CancellationToken cancellationToken = default)
        => await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.UserId == userId
                               && category.Aliases.Any(alias => alias.NormalizedAlias == normalizedAlias))
            .OrderBy(category => category.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => dbContext.Categories.AnyAsync(category => category.UserId == userId, cancellationToken);

    public void Add(BudgetCategory category) => dbContext.Categories.Add(category);
}
