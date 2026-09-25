using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyBudget.Infrastructure.Persistence;

/// <summary>
/// Used only by the <c>dotnet ef</c> tooling (design time), so migrations can be created
/// without booting the API. Runtime wiring lives in <c>DependencyInjection</c>.
/// </summary>
public sealed class MyBudgetDbContextFactory : IDesignTimeDbContextFactory<MyBudgetDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=mybudget;Username=mybudget_migrator;Password=postgres";

    public MyBudgetDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING") ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<MyBudgetDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(MyBudgetDbContext).Assembly.FullName))
            .Options;

        return new MyBudgetDbContext(options);
    }
}
