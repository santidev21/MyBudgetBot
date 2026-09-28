using FluentAssertions;
using MyBudget.Application.Budgets;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Monthly allocations through the real service and repositories.
/// <para>
/// The product rules live in the application layer, so this is where they are checked against a
/// real database: past months are refused, deactivating keeps history, and copying a month
/// produces independent rows rather than a shared reference.
/// </para>
/// </summary>
public sealed class BudgetAllocationTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly MonthPeriod October = new(2026, 10);
    private static readonly MonthPeriod August = new(2026, 8);

    private static BudgetService BuildService(MyBudgetDbContext context) =>
        new(new BudgetRepository(context), new CategoryRepository(context), new UnitOfWork(context));

    [Fact]
    public async Task Setting_an_allocation_persists_the_month()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var result = await BuildService(context).SetAllocationAsync(
            user.Id, September, category.Id, 800_000, Today);

        result.Saved.Should().BeTrue();

        await using var verification = CreateContext();
        var stored = await new BudgetRepository(verification).FindByPeriodAsync(user.Id, September);
        stored!.TotalAllocated.Should().Be(800_000);
    }

    [Fact]
    public async Task Setting_the_same_category_twice_keeps_a_single_row()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.SetAllocationAsync(user.Id, September, category.Id, 800_000, Today);
        await service.SetAllocationAsync(user.Id, September, category.Id, 900_000, Today);

        await using var verification = CreateContext();
        var stored = await new BudgetRepository(verification).FindByPeriodAsync(user.Id, September);
        stored!.Allocations.Should().ContainSingle().Which.Amount.Should().Be(900_000);
    }

    [Fact]
    public async Task Copying_the_previous_month_produces_independent_rows()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var market = TestData.NewCategory(user.Id, "Mercado");
        var transport = TestData.NewCategory(user.Id, "Transporte");
        context.Users.Add(user);
        context.Categories.AddRange(market, transport);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.SetAllocationAsync(user.Id, September, market.Id, 500_000, Today);
        await service.SetAllocationAsync(user.Id, September, transport.Id, 200_000, Today);

        var copied = await service.CopyPreviousMonthAsync(user.Id, October, Today);
        copied.Status.Should().Be(BudgetCopyStatus.Copied);
        copied.Budget!.TotalAllocated.Should().Be(700_000);

        // Editing the copy must not touch the month it was copied from.
        await service.SetAllocationAsync(user.Id, October, market.Id, 1_000_000, Today);

        await using var verification = CreateContext();
        var budgets = new BudgetRepository(verification);
        (await budgets.FindByPeriodAsync(user.Id, September))!.TotalAllocated.Should().Be(700_000);
        (await budgets.FindByPeriodAsync(user.Id, October))!.TotalAllocated.Should().Be(1_200_000);
    }

    [Fact]
    public async Task Copying_without_a_previous_month_writes_nothing()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var result = await BuildService(context).CopyPreviousMonthAsync(user.Id, October, Today);

        result.Status.Should().Be(BudgetCopyStatus.NoPreviousBudget);

        await using var verification = CreateContext();
        (await new BudgetRepository(verification).FindByPeriodAsync(user.Id, October)).Should().BeNull();
    }

    [Fact]
    public async Task A_past_month_is_never_written()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var result = await BuildService(context).SetAllocationAsync(
            user.Id, August, category.Id, 100_000, Today);

        result.Status.Should().Be(BudgetWriteStatus.PastMonth);

        await using var verification = CreateContext();
        (await new BudgetRepository(verification).FindByPeriodAsync(user.Id, August)).Should().BeNull();
    }

    [Fact]
    public async Task The_month_view_keeps_a_deactivated_category_that_holds_an_allocation()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Viajes");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        await service.SetAllocationAsync(user.Id, September, category.Id, 250_000, Today);

        category.Deactivate();
        await context.SaveChangesAsync();

        var view = await service.GetMonthAsync(user.Id, September);

        view.Lines.Should().ContainSingle()
            .Which.Amount.Should().Be(250_000);
        view.Lines[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task An_allocation_for_another_users_category_is_refused()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        var strangerCategory = TestData.NewCategory(stranger.Id, "Mercado");
        context.Users.AddRange(owner, stranger);
        context.Categories.Add(strangerCategory);
        await context.SaveChangesAsync();

        var result = await BuildService(context).SetAllocationAsync(
            owner.Id, September, strangerCategory.Id, 100_000, Today);

        result.Status.Should().Be(BudgetWriteStatus.CategoryNotFound);
    }
}
