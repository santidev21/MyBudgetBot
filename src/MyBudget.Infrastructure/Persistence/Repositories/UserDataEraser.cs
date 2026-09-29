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

        // The erasure runs inside the turn's ambient transaction when there is one (the work
        // lock always wraps a turn), and opens its own only when called standalone. Beginning a
        // second transaction on the same connection would fail.
        var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // Dependency order matters: children before the rows they reference.
            await dbContext.Expenses
                .Where(expense => expense.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.RecurringExpenses
                .Where(rule => rule.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.BudgetAlerts
                .Where(alert => alert.UserId == userId)
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

            await dbContext.BudgetDefaults
                .Where(budgetDefault => budgetDefault.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.Categories
                .Where(category => category.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            await dbContext.Users
                .Where(user => user.Id == userId)
                .ExecuteDeleteAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
