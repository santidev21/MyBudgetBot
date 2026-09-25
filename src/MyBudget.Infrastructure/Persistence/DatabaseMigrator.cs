using Microsoft.EntityFrameworkCore;

namespace MyBudget.Infrastructure.Persistence;

/// <summary>Applies EF Core migrations, safely under concurrent starts.</summary>
public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs migrations while holding a PostgreSQL session advisory lock.
/// <para>
/// Two migrator containers started at the same time (a deploy racing a manual restart)
/// would otherwise both try to apply the schema and one would fail on a half-created
/// object. The lock serialises them; the second one then finds the schema already current.
/// </para>
/// </summary>
internal sealed class DatabaseMigrator(MyBudgetDbContext dbContext) : IDatabaseMigrator
{
    /// <summary>Arbitrary but stable: identifies this application's migration lock.</summary>
    private const long MigrationLockKey = 6_150_202_601L;

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await ExecuteAsync("SELECT pg_advisory_lock(@key)", cancellationToken);

            try
            {
                await dbContext.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await ExecuteAsync("SELECT pg_advisory_unlock(@key)", cancellationToken);
            }
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = MigrationLockKey;
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
