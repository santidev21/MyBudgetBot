using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using NetArchTest.Rules;

namespace MyBudget.Architecture.Tests;

/// <summary>
/// The dependency rule is the whole point of the layering. These tests fail the build when
/// someone reaches for the database from a Telegram handler or imports EF Core into the
/// application layer.
/// </summary>
public sealed class LayeringTests
{
    private const string EntityFramework = "Microsoft.EntityFrameworkCore";
    private const string TelegramSdk = "Telegram.Bot";
    private const string Infrastructure = "MyBudget.Infrastructure";

    [Fact]
    public void The_domain_layer_has_no_outward_dependencies()
    {
        AssertHasNoDependency(
            Assembly.Load("MyBudget.Domain"),
            EntityFramework,
            TelegramSdk,
            Infrastructure);
    }

    [Fact]
    public void The_application_layer_is_free_of_persistence_and_telegram_types()
    {
        // This is what makes the application layer testable without Telegram and without a
        // database: it may only depend on the domain.
        AssertHasNoDependency(
            Assembly.Load("MyBudget.Application"),
            EntityFramework,
            TelegramSdk,
            Infrastructure);
    }

    [Fact]
    public void The_telegram_layer_does_not_talk_to_the_database_directly()
    {
        // Handlers call application use cases; they never open a DbContext.
        AssertHasNoDependency(
            Assembly.Load("MyBudget.Telegram"),
            EntityFramework,
            Infrastructure);
    }

    [Fact]
    public void The_infrastructure_layer_does_not_know_about_telegram()
    {
        AssertHasNoDependency(
            Assembly.Load("MyBudget.Infrastructure"),
            TelegramSdk);
    }

    [Fact]
    public void Entities_live_in_the_domain_and_derive_from_the_entity_base()
    {
        var entityTypes = Assembly.Load("MyBudget.Domain")
            .GetTypes()
            .Where(type => type.IsClass
                           && !type.IsAbstract
                           && !type.IsNested
                           && type.GetCustomAttribute<CompilerGeneratedAttribute>() is null
                           && type.Namespace is not null
                           && type.Namespace.StartsWith("MyBudget.Domain.", StringComparison.Ordinal)
                           && type.Namespace is not "MyBudget.Domain.Common")
            .ToList();

        entityTypes.Should().NotBeEmpty();

        var offenders = entityTypes
            .Where(type => !typeof(MyBudget.Domain.Common.Entity).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToList();

        offenders.Should().BeEmpty("every domain type must derive from Entity");
    }

    private static void AssertHasNoDependency(Assembly assembly, params string[] forbiddenNamespaces)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty(
            $"{assembly.GetName().Name} must not depend on: {string.Join(", ", forbiddenNamespaces)}");
    }
}
