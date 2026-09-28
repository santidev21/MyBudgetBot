using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;

namespace MyBudget.Application.Tests.Abstractions;

/// <summary>
/// Ownership is enforced by making the owner id the first argument of every scoped query.
/// This test keeps that convention from silently eroding as new methods are added.
/// Mutating methods (Add/Remove) are exempt: the entity itself carries its UserId and the
/// database enforces that the owner matches through composite foreign keys.
/// </summary>
public sealed class RepositoryContractTests
{
    [Theory]
    [InlineData(typeof(ICategoryRepository))]
    [InlineData(typeof(IExpenseRepository))]
    [InlineData(typeof(IExpenseReadRepository))]
    [InlineData(typeof(IBudgetRepository))]
    public void Every_query_requires_the_owner_id_as_the_first_parameter(Type repositoryInterface)
    {
        repositoryInterface.IsInterface.Should().BeTrue();

        var queryMethods = repositoryInterface
            .GetMethods()
            .Where(method => method.Name.StartsWith("Find", StringComparison.Ordinal)
                             || method.Name.StartsWith("List", StringComparison.Ordinal)
                             || method.Name.StartsWith("Any", StringComparison.Ordinal))
            .ToList();

        queryMethods.Should().NotBeEmpty($"{repositoryInterface.Name} must expose finders or listings");

        foreach (var method in queryMethods)
        {
            var firstParameter = method.GetParameters().FirstOrDefault();

            firstParameter.Should().NotBeNull(
                $"{repositoryInterface.Name}.{method.Name} must take the owner id");
            firstParameter!.Name.Should().Be(
                "userId", $"{repositoryInterface.Name}.{method.Name} must name it userId");
            firstParameter.ParameterType.Should().Be(
                typeof(Guid), $"{repositoryInterface.Name}.{method.Name} must scope by Guid userId");
        }
    }

    [Fact]
    public void The_user_repository_resolves_identity_by_telegram_id_not_by_username()
    {
        typeof(IUserRepository).GetMethods()
            .Should().Contain(method => method.Name == "FindByTelegramUserIdAsync");

        typeof(IUserRepository).GetMethods()
            .SelectMany(method => method.GetParameters())
            .Should().NotContain(parameter => parameter.Name == "username");
    }
}
