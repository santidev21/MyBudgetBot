using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.Presentation;
using Telegram.Bot;

namespace MyBudget.Telegram;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Telegram presentation layer.
    /// <para>
    /// Without a bot token the layer still resolves — conversations and the dispatcher are
    /// wired — but no API client is created and replies are dropped with a warning. That is
    /// what lets the service run for migrations and health checks without credentials, and
    /// lets the pipeline be tested without a token.
    /// </para>
    /// </summary>
    public static IServiceCollection AddTelegram(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<TelegramOptions>, TelegramOptionsValidator>();

        var options = configuration
            .GetSection(TelegramOptions.SectionName)
            .Get<TelegramOptions>() ?? new TelegramOptions();

        if (options.IsEnabled)
        {
            services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(options.BotToken));
            services.AddSingleton<ITelegramSender, TelegramSender>();
            services.AddSingleton<ITelegramProvisioner, TelegramProvisioner>();

            // Registered here so polling simply does not run when webhooks are the transport.
            services.AddHostedService<TelegramPollingService>();
        }
        else
        {
            services.AddSingleton<ITelegramSender, NullTelegramSender>();
        }

        services.AddSingleton<MainMenu>();
        services.AddScoped<IConversation, StartConversation>();
        services.AddScoped<IConversation, CategoriesConversation>();
        services.AddScoped<ConversationRouter>();
        services.AddScoped<ITelegramUpdateDispatcher, TelegramUpdateDispatcher>();

        return services;
    }
}
