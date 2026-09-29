using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class UserDataEraser(MyBudgetDbContext dbContext) : IUserDataEraser
{
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        // Dependency order matters: children before the rows they reference.
        await dbContext.Expenses
            .Where(expense => expense.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.RecurringExpenses
            .Where(rule => rule.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.CategoryAliases
            .Where(alias => alias.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.MonthlyBudgetCategories
            .Where(allocation => allocation.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.MonthlyBudgets
            .Where(budget => budget.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Categories
            .Where(category => category.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Users
            .Where(user => user.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
