using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using Npgsql;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class UnitOfWork(MyBudgetDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (TryFindUniqueViolation(exception, out var constraintName))
        {
            // Translating here is what keeps the application layer free of provider details.
            throw new UniqueConstraintViolationException(constraintName!, exception);
        }
    }

    public void Detach(object entity) => dbContext.Entry(entity).State = EntityState.Detached;

    private static bool TryFindUniqueViolation(Exception exception, out string? constraintName)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
            {
                constraintName = postgres.ConstraintName;
                return true;
            }
        }

        constraintName = null;
        return false;
    }
}
