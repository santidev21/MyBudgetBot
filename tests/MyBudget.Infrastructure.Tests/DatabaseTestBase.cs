using Microsoft.EntityFrameworkCore;
using MyBudget.Infrastructure.Persistence;
using Npgsql;

namespace MyBudget.Infrastructure.Tests;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "postgres-database";
}

[Collection(DatabaseCollection.Name)]
public abstract class DatabaseTestBase(DatabaseFixture fixture) : IAsyncLifetime
{
    protected const string UniqueViolation = "23505";
    protected const string ForeignKeyViolation = "23503";
    protected const string CheckViolation = "23514";
    protected const string NotNullViolation = "23502";

    protected DatabaseFixture Fixture { get; } = fixture;

    protected MyBudgetDbContext CreateContext() => Fixture.CreateContext();

    /// <summary>Starts every test from an empty schema so tests cannot leak state into each other.</summary>
    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Runs an operation that is expected to be rejected by the database and returns the
    /// PostgreSQL error, unwrapping whatever EF put around it.
    /// </summary>
    protected static async Task<PostgresException> ExpectDatabaseErrorAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (FindPostgresException(exception) is { } postgresException)
        {
            return postgresException;
        }

        throw new Xunit.Sdk.XunitException(
            "Expected the database to reject the operation, but it succeeded.");
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }
}
