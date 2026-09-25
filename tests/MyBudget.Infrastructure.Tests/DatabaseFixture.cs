using Microsoft.EntityFrameworkCore;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Interceptors;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// One real PostgreSQL container per test run, migrated once, reset between tests.
/// The EF Core in-memory provider is never used: it cannot enforce CHECK, UNIQUE or
/// FOREIGN KEY constraints, which are exactly the guarantees under test here.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;
    private Respawner _respawner = null!;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("mybudget")
            .WithUsername("mybudget")
            .WithPassword("mybudget")
            .Build();

        await _container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            // Never wipe the migration history, or subsequent resets break the schema state.
            TablesToIgnore = [new Table("__EFMigrationsHistory")],
        });
    }

    public MyBudgetDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<MyBudgetDbContext>()
            .UseNpgsql(ConnectionString)
            .AddInterceptors(new AuditableEntityInterceptor(TimeProvider.System))
            .Options;

        return new MyBudgetDbContext(options);
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
