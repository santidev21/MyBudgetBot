using FluentAssertions;
using MyBudget.Application.Expenses;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Expense persistence through the real service and repositories.
/// <para>
/// The conversation tests cover what the user sees; these prove the writes actually land, that
/// an edit rewrites the row rather than inserting, and that a category belonging to somebody
/// else is refused before the database even sees it.
/// </para>
/// </summary>
public sealed class ExpensePersistenceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly MonthPeriod September = new(2026, 9);

    private static ExpenseService BuildService(MyBudgetDbContext context) =>
        new(new ExpenseRepository(context), new CategoryRepository(context), new UnitOfWork(context));

    [Fact]
    public async Task Creating_an_expense_persists_it_in_the_month()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var result = await BuildService(context).CreateAsync(
            user.Id, category.Id, 35_000, "Verduras", new DateOnly(2026, 9, 27), Today);

        result.Saved.Should().BeTrue();

        await using var verification = CreateContext();
        var items = await BuildService(verification).ListMonthAsync(user.Id, September);
        items.Should().ContainSingle()
            .Which.Amount.Should().Be(35_000);
    }

    [Fact]
    public async Task Updating_an_expense_rewrites_the_row()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var created = await service.CreateAsync(
            user.Id, category.Id, 35_000, "Verduras", new DateOnly(2026, 9, 27), Today);

        await service.UpdateAsync(
            user.Id, created.Expense!.Id, category.Id, 40_000, "Frutas", new DateOnly(2026, 9, 26), Today);

        await using var verification = CreateContext();
        var items = await BuildService(verification).ListMonthAsync(user.Id, September);
        items.Should().ContainSingle();
        items[0].Amount.Should().Be(40_000);
        items[0].Description.Should().Be("Frutas");
    }

    [Fact]
    public async Task Deleting_an_expense_removes_it()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var created = await service.CreateAsync(
            user.Id, category.Id, 35_000, "Verduras", new DateOnly(2026, 9, 27), Today);

        (await service.DeleteAsync(user.Id, created.Expense!.Id)).Should().BeTrue();

        await using var verification = CreateContext();
        (await BuildService(verification).ListMonthAsync(user.Id, September)).Should().BeEmpty();
    }

    [Fact]
    public async Task An_expense_for_another_users_category_is_refused()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        var strangerCategory = TestData.NewCategory(stranger.Id, "Mercado");
        context.Users.AddRange(owner, stranger);
        context.Categories.Add(strangerCategory);
        await context.SaveChangesAsync();

        var result = await BuildService(context).CreateAsync(
            owner.Id, strangerCategory.Id, 35_000, "Verduras", new DateOnly(2026, 9, 27), Today);

        result.Status.Should().Be(ExpenseChangeStatus.CategoryNotFound);

        await using var verification = CreateContext();
        (await BuildService(verification).ListMonthAsync(owner.Id, September)).Should().BeEmpty();
    }
}
