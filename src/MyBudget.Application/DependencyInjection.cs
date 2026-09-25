using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyBudget.Application.Configuration;

namespace MyBudget.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers application configuration. Validation runs at startup so a misconfigured
    /// deployment fails immediately instead of failing for the first user who talks to the
    /// bot.
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

        return services;
    }
}
