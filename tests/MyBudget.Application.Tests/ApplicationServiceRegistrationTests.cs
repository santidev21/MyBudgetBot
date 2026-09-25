using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBudget.Application;
using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;

namespace MyBudget.Application.Tests;

/// <summary>
/// The service graph, validated as it is built.
/// <para>
/// A missing registration is otherwise discovered by whoever first calls the dependency, in
/// production. <c>ValidateOnBuild</c> turns that into a build-time failure here.
/// </para>
/// </summary>
public sealed class ApplicationServiceRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Localization:DefaultLanguage"] = "es",
                ["Localization:DefaultTimeZone"] = "America/Bogota",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddApplication(configuration);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Fact]
    public void Every_registered_service_can_be_constructed()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IUserMessages>().Should().BeOfType<ResourceUserMessages>();
        provider.GetRequiredService<ICurrencyRegistry>().All.Should().NotBeEmpty();
        provider.GetRequiredService<IMoneyFormatter>().Should().BeOfType<MoneyFormatter>();
        provider.GetRequiredService<IMoneyParser>().Should().BeOfType<MoneyParser>();
        provider.GetRequiredService<ICompactExpenseParser>().Should().BeOfType<CompactExpenseParser>();
        provider.GetRequiredService<IDateParser>().Should().BeOfType<DateParser>();
    }

    [Fact]
    public void The_registered_parser_works_end_to_end_through_the_container()
    {
        using var provider = BuildProvider();

        var parser = provider.GetRequiredService<IMoneyParser>();
        var formatter = provider.GetRequiredService<IMoneyFormatter>();

        var prompt = provider.GetRequiredService<IUserMessages>();
        prompt.Get("es", MessageKeys.AmountPrompt).Should().Be("¿Cuánto gastaste?");

        var result = parser.Parse("35 mil", "COP");
        result.Should().BeOfType<MoneyParseResult.Success>();
        ((MoneyParseResult.Success)result).Display.Should().Be(formatter.Format(35_000, "COP"));
    }

    [Fact]
    public void The_input_services_are_shared_because_they_are_stateless()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IMoneyParser>()
            .Should().BeSameAs(provider.GetRequiredService<IMoneyParser>());
    }
}
