using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyBudget.Application.Budgets;
using MyBudget.Application.Categories;
using MyBudget.Application.Configuration;
using MyBudget.Application.Dates;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Users;

namespace MyBudget.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application configuration and the pure input-handling services.
    /// <para>
    /// Validation runs at startup so a misconfigured deployment fails immediately instead
    /// of failing for the first user who talks to the bot. Everything registered here is
    /// stateless, hence singleton.
    /// </para>
    /// </summary>
    public static IServiceCollection AddApplication(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<LocalizationOptions>()
            .Bind(configuration.GetSection(LocalizationOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>();

        services.AddSingleton<IUserMessages, ResourceUserMessages>();
        services.AddSingleton<ICurrencyRegistry>(_ => new CurrencyRegistry());
        services.AddSingleton<IMoneyFormatter, MoneyFormatter>();
        services.AddSingleton<IMoneyParser, MoneyParser>();
        services.AddSingleton<ICompactExpenseParser, CompactExpenseParser>();
        services.AddSingleton<IDateParser, DateParser>();
        services.AddScoped<IUserLocalDate, UserLocalDate>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IExpenseService, ExpenseService>();

        return services;
    }
}
