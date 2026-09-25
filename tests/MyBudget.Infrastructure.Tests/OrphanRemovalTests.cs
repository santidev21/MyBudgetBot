using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Removing a child from an aggregate navigation must delete the row.
/// EF Core decides this from the relationship's optionality: because the foreign key is
/// non-nullable, a child dropped from its parent is treated as an orphan and deleted.
/// That behaviour is a convention, not something the domain expresses, so it is pinned
/// here rather than assumed.
/// </summary>
public sealed class OrphanRemovalTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Removing_an_allocation_deletes_the_row()
    {
        Guid userId;
        Guid categoryId;

        await using (var context = CreateContext())
        {
            var user = TestData.NewUser();
            var category = TestData.NewCategory(user.Id, "Mercado");
            var budget = TestData.NewBudget(user.Id, 2026, 9);
            budget.SetAllocation(category.Id, 1_000_000);
            userId = user.Id;
            categoryId = category.Id;

            context.Users.Add(user);
            context.Categories.Add(category);
            context.MonthlyBudgets.Add(budget);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var budget = await new BudgetRepository(context)
                .FindByPeriodAsync(userId, new MonthPeriod(2026, 9));

            budget.Should().NotBeNull();
            var removed = budget!.RemoveAllocation(categoryId);

            removed.Should().BeTrue();
            await context.SaveChangesAsync();
        }

        await using (var verification = CreateContext())
        {
            (await verification.MonthlyBudgetCategories.CountAsync()).Should().Be(0);

            var reloaded = await new BudgetRepository(verification)
                .FindByPeriodAsync(userId, new MonthPeriod(2026, 9));

            reloaded!.Allocations.Should().BeEmpty();
            reloaded.TotalAllocated.Should().Be(0);
        }
    }

    [Fact]
    public async Task Removing_an_alias_deletes_the_row()
    {
        Guid userId;
        Guid categoryId;

        await using (var context = CreateContext())
        {
            var user = TestData.NewUser();
            var category = TestData.NewCategory(user.Id, "Mercado");
            category.AddAlias("Verduras", "verduras");
            userId = user.Id;
            categoryId = category.Id;

            context.Users.Add(user);
            context.Categories.Add(category);
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var category = await new CategoryRepository(context)
                .FindByIdAsync(userId, categoryId);

            category.Should().NotBeNull();
            var alias = category!.Aliases.Should().ContainSingle().Subject;
            alias.Id.Should().NotBe(Guid.Empty, "the persistence layer assigns the key on insert");

            category.RemoveAlias(alias).Should().BeTrue();
            await context.SaveChangesAsync();
        }

        await using (var verification = CreateContext())
        {
            (await verification.CategoryAliases.CountAsync()).Should().Be(0);
        }
    }
}
