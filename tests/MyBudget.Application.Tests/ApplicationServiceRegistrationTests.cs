using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyBudget.Application;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Budgets;
using MyBudget.Application.Categories;
using MyBudget.Application.Dates;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Matching;
using MyBudget.Application.Money;
using MyBudget.Application.Users;
using NSubstitute;

namespace MyBudget.Application.Tests;

/// <summary>
/// The service graph, validated as it is built.
/// <para>
/// A missing registration is otherwise discovered by whoever first calls the dependency, in
/// production. <c>ValidateOnBuild</c> turns that into a failure here.
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

        // The application layer owns these contracts; their implementations live in
        // infrastructure. Substitutes keep this test about the application graph only.
        services.AddScoped(_ => Substitute.For<IUserRepository>());
        services.AddScoped(_ => Substitute.For<ICategoryRepository>());
        services.AddScoped(_ => Substitute.For<IBudgetRepository>());
        services.AddScoped(_ => Substitute.For<IExpenseRepository>());
        services.AddScoped(_ => Substitute.For<IUnitOfWork>());
        services.AddSingleton(TimeProvider.System);

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
        using var scope = provider.CreateScope();

        // Scoped services resolve from a scope, exactly as they do in a request.
        scope.ServiceProvider.GetRequiredService<IUserMessages>().Should().BeOfType<ResourceUserMessages>();
        scope.ServiceProvider.GetRequiredService<ICurrencyRegistry>().All.Should().NotBeEmpty();
        scope.ServiceProvider.GetRequiredService<IMoneyFormatter>().Should().BeOfType<MoneyFormatter>();
        scope.ServiceProvider.GetRequiredService<IMoneyParser>().Should().BeOfType<MoneyParser>();
        scope.ServiceProvider.GetRequiredService<ICompactExpenseParser>().Should().BeOfType<CompactExpenseParser>();
        scope.ServiceProvider.GetRequiredService<ICategoryMatcher>().Should().BeOfType<CategoryMatcher>();
        scope.ServiceProvider.GetRequiredService<IDateParser>().Should().BeOfType<DateParser>();
        scope.ServiceProvider.GetRequiredService<IUserService>().Should().BeOfType<UserService>();
        scope.ServiceProvider.GetRequiredService<ICategoryService>().Should().BeOfType<CategoryService>();
        scope.ServiceProvider.GetRequiredService<IBudgetService>().Should().BeOfType<BudgetService>();
        scope.ServiceProvider.GetRequiredService<IExpenseService>().Should().BeOfType<ExpenseService>();
        scope.ServiceProvider.GetRequiredService<IUserLocalDate>().Should().BeOfType<UserLocalDate>();
    }

    [Fact]
    public void The_registered_parser_works_end_to_end_through_the_container()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var parser = scope.ServiceProvider.GetRequiredService<IMoneyParser>();
        var formatter = scope.ServiceProvider.GetRequiredService<IMoneyFormatter>();
        var prompt = scope.ServiceProvider.GetRequiredService<IUserMessages>();

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
