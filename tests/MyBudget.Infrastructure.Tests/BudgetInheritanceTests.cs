using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Budgets;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The budget-inheritance contract end to end against PostgreSQL.
/// <para>
/// A budget reaches the next month through <c>budget_defaults</c>, and the one-off backfill is
/// what turns a legacy per-month allocation into that default. These tests fail if a future
/// change stops a later month from inheriting, which is exactly the October regression.
/// </para>
/// </summary>
public sealed class BudgetInheritanceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly MonthPeriod August = new(2026, 8);
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly MonthPeriod October = new(2026, 10);

    [Fact]
    public async Task The_backfill_turns_the_latest_allocation_of_each_category_into_a_default()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var food = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(food);
        await context.SaveChangesAsync();

        AddAllocation(context, user.Id, food.Id, September, 900_000);
        AddAllocation(context, user.Id, food.Id, October, 1_500_000);
        await context.SaveChangesAsync();

        await RunBackfillAsync(context);

        var defaults = await new BudgetRepository(context).ListDefaultsAsync(user.Id);

        defaults.Should().ContainSingle();
        defaults[0].CategoryId.Should().Be(food.Id);
        // The newest allocation wins; the default starts there, never earlier.
        defaults[0].Amount.Should().Be(1_500_000);
        defaults[0].EffectiveFrom.Should().Be(October);
    }

    [Fact]
    public async Task The_next_month_inherits_the_backfilled_budget_and_an_earlier_one_does_not()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var food = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(food);
        await context.SaveChangesAsync();

        AddAllocation(context, user.Id, food.Id, September, 1_000_000);
        await context.SaveChangesAsync();

        await RunBackfillAsync(context);

        var service = new BudgetService(
            new BudgetRepository(context), new CategoryRepository(context), new UnitOfWork(context));

        var august = await service.GetMonthAsync(user.Id, August);
        var september = await service.GetMonthAsync(user.Id, September);
        var october = await service.GetMonthAsync(user.Id, October);

        // Before the default existed: nothing is invented retroactively.
        august.TotalAllocated.Should().Be(0);

        // The month the user set keeps its own snapshot.
        september.TotalAllocated.Should().Be(1_000_000);
        september.Lines.Single().IsRecurring.Should().BeFalse();

        // And the next month inherits it without the user doing anything.
        october.TotalAllocated.Should().Be(1_000_000);
        october.Lines.Single().IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task Running_the_backfill_twice_does_not_duplicate_a_default()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var food = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(food);
        await context.SaveChangesAsync();

        AddAllocation(context, user.Id, food.Id, September, 1_000_000);
        await context.SaveChangesAsync();

        await RunBackfillAsync(context);
        await RunBackfillAsync(context);

        var defaults = await new BudgetRepository(context).ListDefaultsAsync(user.Id);
        defaults.Should().ContainSingle();
    }

    [Fact]
    public async Task A_category_that_was_never_assigned_gets_no_default()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var funded = TestData.NewCategory(user.Id, "Mercado");
        var neverFunded = TestData.NewCategory(user.Id, "Ocio", "🎬");
        context.Users.Add(user);
        context.Categories.Add(funded);
        context.Categories.Add(neverFunded);
        await context.SaveChangesAsync();

        AddAllocation(context, user.Id, funded.Id, September, 1_000_000);
        await context.SaveChangesAsync();

        await RunBackfillAsync(context);

        var defaults = await new BudgetRepository(context).ListDefaultsAsync(user.Id);
        defaults.Should().ContainSingle().Which.CategoryId.Should().Be(funded.Id);
    }

    private static void AddAllocation(
        MyBudgetDbContext context, Guid userId, Guid categoryId, MonthPeriod period, long amount)
    {
        var budget = TestData.NewBudget(userId, period.Year, period.Month);
        budget.SetAllocation(categoryId, amount);
        context.MonthlyBudgets.Add(budget);
    }

    private static Task RunBackfillAsync(MyBudgetDbContext context) =>
        context.Database.ExecuteSqlRawAsync(BudgetDefaultsBackfill.Sql);
}
