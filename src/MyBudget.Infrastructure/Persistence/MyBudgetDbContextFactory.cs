using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MyBudget.Infrastructure.Configuration;

namespace MyBudget.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling (design time), so migrations can be created
/// without booting the API. Runtime wiring lives in <c>DependencyInjection</c>.
/// </summary>
public sealed class MyBudgetDbContextFactory : IDesignTimeDbContextFactory<MyBudgetDbContext>
{
    /// <summary>
    /// Local development only. Uses the migrator role because applying migrations needs DDL,
    /// while the runtime role deliberately has none. Port 5435 follows the host convention:
    /// 5432 is the default every tool grabs, and 5433/5434 are taken by other projects.
    /// </summary>
    private const string FallbackConnectionString =
        "Host=localhost;Port=5435;Database=mybudget;Username=mybudget_migrator;Password=postgres";

    public MyBudgetDbContext CreateDbContext(string[] args)
    {
        // A design-time run has no host to read configuration from, so the value is taken
        // from the environment or, failing that, from .env. Both before the local fallback.
        var connectionString = FirstNonEmpty(
            Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING"),
            DotEnvFile.Load().GetValueOrDefault("DATABASE_CONNECTION_STRING"),
            FallbackConnectionString);

        var options = new DbContextOptionsBuilder<MyBudgetDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(MyBudgetDbContext).Assembly.FullName))
            .Options;

        return new MyBudgetDbContext(options);
    }

    private static string FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)) ?? string.Empty;
}
