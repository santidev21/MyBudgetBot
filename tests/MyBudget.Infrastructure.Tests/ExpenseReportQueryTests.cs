using FluentAssertions;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The reporting queries against real PostgreSQL.
/// <para>
/// These prove the aggregations PostgreSQL actually runs: grouped sums and counts, user
/// isolation, and the keyset page boundary. The page query is raw SQL precisely because the
/// tiebreaker is a <c>uuid</c>, so its ordering and boundary are the thing under test.
/// </para>
/// </summary>
public sealed class ExpenseReportQueryTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly DateRange SeptemberRange = DateRange.ForMonth(September);

    [Fact]
    public async Task Sums_are_grouped_by_category_and_day_and_scoped_to_the_user()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        var food = new BudgetCategory(owner.Id, "Comida", "🍔");
        var transport = new BudgetCategory(owner.Id, "Transporte", "🚕");
        var strangerCategory = new BudgetCategory(stranger.Id, "Viajes", "✈️");
        context.Users.AddRange(owner, stranger);
        context.Categories.AddRange(food, transport, strangerCategory);

        context.Expenses.AddRange(
            new Expense(owner.Id, food.Id, 30_000, "Almuerzo", new DateOnly(2026, 9, 10), TestData.Today),
            new Expense(owner.Id, food.Id, 20_000, "Cena", new DateOnly(2026, 9, 10), TestData.Today),
            new Expense(owner.Id, transport.Id, 15_000, "Taxi", new DateOnly(2026, 9, 11), TestData.Today),
            new Expense(owner.Id, food.Id, 99_000, "Agosto", new DateOnly(2026, 8, 31), TestData.Today),
            new Expense(stranger.Id, strangerCategory.Id, 500_000, "Ajeno", new DateOnly(2026, 9, 10), TestData.Today));
        await context.SaveChangesAsync();

        var queries = new ExpenseReadRepository(context);

        (await queries.SumAsync(owner.Id, SeptemberRange)).Should().Be(65_000);

        var byCategory = await queries.SumByCategoryAsync(owner.Id, SeptemberRange);
        byCategory.Should().HaveCount(2);
        byCategory.Single(total => total.CategoryId == food.Id).Should()
            .Be(new CategoryTotal(food.Id, 50_000, 2));
        byCategory.Single(total => total.CategoryId == transport.Id).Should()
            .Be(new CategoryTotal(transport.Id, 15_000, 1));

        var byDay = await queries.SumByDayAsync(owner.Id, SeptemberRange);
        byDay.Should().HaveCount(2);
        byDay.Single(total => total.Date == new DateOnly(2026, 9, 10)).Should()
            .Be(new DailyTotal(new DateOnly(2026, 9, 10), 50_000, 2));
        byDay.Single(total => total.Date == new DateOnly(2026, 9, 11)).Should()
            .Be(new DailyTotal(new DateOnly(2026, 9, 11), 15_000, 1));
    }

    [Fact]
    public async Task The_keyset_page_walks_the_whole_range_without_gaps_or_duplicates()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);

        // Several expenses share a date so the uuid tiebreaker is exercised, not just the date.
        var dates = new[]
        {
            new DateOnly(2026, 9, 5),
            new DateOnly(2026, 9, 5),
            new DateOnly(2026, 9, 5),
            new DateOnly(2026, 9, 12),
            new DateOnly(2026, 9, 12),
            new DateOnly(2026, 9, 20),
            new DateOnly(2026, 9, 30),
        };
        for (var index = 0; index < dates.Length; index++)
        {
            context.Expenses.Add(new Expense(
                user.Id, category.Id, 1_000 * (index + 1), $"Gasto {index}", dates[index], TestData.Today));
        }

        await context.SaveChangesAsync();

        var queries = new ExpenseReadRepository(context);
        var expected = await queries.ListPageAsync(user.Id, SeptemberRange, after: null, take: 100);

        var walked = new List<Expense>();
        ExpensePageCursor? cursor = null;
        for (var guard = 0; guard < 10; guard++)
        {
            var page = await queries.ListPageAsync(user.Id, SeptemberRange, cursor, take: 3);
            walked.AddRange(page);
            if (page.Count < 3)
            {
                break;
            }

            cursor = new ExpensePageCursor(page[^1].ExpenseDate, page[^1].Id);
        }

        walked.Select(expense => expense.Id).Should().Equal(expected.Select(expense => expense.Id));
        walked.Should().HaveCount(dates.Length);

        // Newest first by (expense_date DESC, id DESC).
        for (var index = 1; index < walked.Count; index++)
        {
            var previous = walked[index - 1];
            var current = walked[index];
            var ordered = previous.ExpenseDate > current.ExpenseDate
                          || (previous.ExpenseDate == current.ExpenseDate
                              && previous.Id.CompareTo(current.Id) > 0);
            ordered.Should().BeTrue("the page order must be strictly descending");
        }
    }

    [Fact]
    public async Task The_largest_expenses_come_back_biggest_first()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        context.Expenses.AddRange(
            new Expense(user.Id, category.Id, 10_000, "Pequeño", new DateOnly(2026, 9, 2), TestData.Today),
            new Expense(user.Id, category.Id, 90_000, "Grande", new DateOnly(2026, 9, 3), TestData.Today),
            new Expense(user.Id, category.Id, 50_000, "Medio", new DateOnly(2026, 9, 4), TestData.Today));
        await context.SaveChangesAsync();

        var largest = await new ExpenseReadRepository(context)
            .ListLargestAsync(user.Id, SeptemberRange, take: 2);

        largest.Select(expense => expense.Amount).Should().Equal(90_000, 50_000);
    }

    [Fact]
    public async Task The_range_is_inclusive_and_excludes_neighbouring_months()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        context.Expenses.AddRange(
            new Expense(user.Id, category.Id, 1_000, "Primero", new DateOnly(2026, 9, 1), TestData.Today),
            new Expense(user.Id, category.Id, 1_000, "Último", new DateOnly(2026, 9, 30), TestData.Today),
            new Expense(user.Id, category.Id, 1_000, "Antes", new DateOnly(2026, 8, 31), TestData.Today),
            new Expense(user.Id, category.Id, 1_000, "Después", new DateOnly(2026, 10, 1), TestData.Today));
        await context.SaveChangesAsync();

        (await new ExpenseReadRepository(context).SumAsync(user.Id, SeptemberRange)).Should().Be(2_000);
    }
}
