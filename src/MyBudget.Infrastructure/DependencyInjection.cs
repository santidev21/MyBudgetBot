using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Interceptors;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the PostgreSQL persistence stack. The connection string is required:
    /// failing fast at startup is preferable to a half-configured running service.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ConnectionStrings:Database' is not configured.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditableEntityInterceptor>();

        services.AddDbContext<MyBudgetDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserDataEraser, UserDataEraser>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IBudgetRepository, BudgetRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<IUpdateInbox, UpdateInbox>();
        services.AddScoped<IConversationStore, ConversationStore>();
        services.AddScoped<IPendingActionStore, PendingActionStore>();
        services.AddScoped<IUserWorkLock, UserWorkLock>();

        // Keeps the update inbox from growing for the lifetime of the deployment.
        services.AddHostedService<UpdateInboxCleanupService>();

        return services;
    }
}
