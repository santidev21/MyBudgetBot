using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Budgets;
using MyBudget.Application.Dates;
using MyBudget.Application.Expenses;
using MyBudget.Application.Recurring;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Recurring;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;
using NSubstitute;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Recurring-rule persistence, ownership and the nightly application pass.
/// <para>
/// The domain tests prove the calendar arithmetic; these prove the rule lands in the database,
/// that one user's rule can never point at another user's category, and that applying it is
/// idempotent: a second run must not duplicate the month's expense.
/// </para>
/// </summary>
public sealed class RecurringExpensePersistenceTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static RecurringExpenseService BuildService(MyBudgetDbContext context, TimeProvider? clock = null) =>
        new(
            new RecurringExpenseRepository(context),
            new ExpenseRepository(context),
            new CategoryRepository(context),
            new UserRepository(context),
            SilentAlerts(),
            new UserLocalDate(clock ?? TimeProvider.System),
            new UnitOfWork(context));

    private static IBudgetAlertService SilentAlerts()
    {
        var alerts = Substitute.For<IBudgetAlertService>();
        alerts.EvaluateAsync(Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetAlert>>([]));
        return alerts;
    }

    private static RecurringExpense NewRule(
        Guid userId,
        Guid categoryId,
        int dayOfMonth = 1,
        DateOnly? startDate = null,
        long amount = 900_000,
        string? description = "Arriendo") =>
        new(userId, categoryId, amount, description, dayOfMonth, startDate ?? new DateOnly(2026, 9, 1));

    [Fact]
    public async Task A_rule_is_created_and_listed_with_its_category()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Arriendo");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var result = await BuildService(context).CreateAsync(
            user.Id, category.Id, 900_000, "Arriendo", 1, new DateOnly(2026, 9, 1));

        result.Saved.Should().BeTrue();

        await using var verification = CreateContext();
        var views = await BuildService(verification).ListAsync(user.Id);
        views.Should().ContainSingle();
        views[0].CategoryName.Should().Be("Arriendo");
        views[0].Amount.Should().Be(900_000);
        views[0].DayOfMonth.Should().Be(1);
        views[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task A_rule_for_another_users_category_is_refused()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        var strangersCategory = TestData.NewCategory(stranger.Id, "Mercado");
        context.Users.AddRange(owner, stranger);
        context.Categories.Add(strangersCategory);
        await context.SaveChangesAsync();

        var result = await BuildService(context).CreateAsync(
            owner.Id, strangersCategory.Id, 35_000, "Verduras", 1, new DateOnly(2026, 9, 1));

        result.Status.Should().Be(RecurringChangeStatus.CategoryNotFound);
    }

    [Fact]
    public async Task A_rule_cannot_reference_a_category_owned_by_another_user_in_the_database()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var intruder = TestData.NewUser(2);
        var ownersCategory = TestData.NewCategory(owner.Id, "Mercado");
        context.Users.AddRange(owner, intruder);
        context.Categories.Add(ownersCategory);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO recurring_expenses
                    (id, user_id, category_id, amount, description, day_of_month,
                     start_date, end_date, is_active, last_generated_date, created_at, updated_at)
                VALUES
                    (gen_random_uuid(), {intruder.Id}, {ownersCategory.Id}, 900000, 'Arriendo', 1,
                     DATE '2026-09-01', NULL, true, NULL, now(), now())
                """);
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_recurring_expenses_category_same_user");
    }

    [Theory]
    [InlineData("amount", "0", "ck_recurring_expenses_amount")]
    [InlineData("day", "0", "ck_recurring_expenses_day")]
    [InlineData("day", "32", "ck_recurring_expenses_day")]
    public async Task The_database_refuses_a_rule_the_domain_forbids(string column, string value, string constraint)
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var amount = column == "amount" ? value : "900000";
        var day = column == "day" ? value : "1";

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO recurring_expenses
                    (id, user_id, category_id, amount, description, day_of_month,
                     start_date, end_date, is_active, last_generated_date, created_at, updated_at)
                VALUES
                    (gen_random_uuid(), {user.Id}, {category.Id}, {amount}::bigint, 'Arriendo', {day}::smallint,
                     DATE '2026-09-01', NULL, true, NULL, now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be(constraint);
    }

    [Fact]
    public async Task The_database_refuses_an_end_date_before_the_start()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO recurring_expenses
                    (id, user_id, category_id, amount, description, day_of_month,
                     start_date, end_date, is_active, last_generated_date, created_at, updated_at)
                VALUES
                    (gen_random_uuid(), {user.Id}, {category.Id}, 900000, 'Arriendo', 1,
                     DATE '2026-09-01', DATE '2026-08-01', true, NULL, now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_recurring_expenses_period");
    }

    [Fact]
    public async Task One_users_rules_are_never_listed_for_another()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        var ownersCategory = TestData.NewCategory(owner.Id);
        var strangersCategory = TestData.NewCategory(stranger.Id, "Transporte");
        context.Users.AddRange(owner, stranger);
        context.Categories.AddRange(ownersCategory, strangersCategory);
        await context.SaveChangesAsync();

        context.RecurringExpenses.Add(NewRule(owner.Id, ownersCategory.Id));
        context.RecurringExpenses.Add(NewRule(stranger.Id, strangersCategory.Id, description: "Bus"));
        await context.SaveChangesAsync();

        await using var verification = CreateContext();
        var views = await BuildService(verification).ListAsync(owner.Id);

        views.Should().ContainSingle().Which.Description.Should().Be("Arriendo");
    }

    [Fact]
    public async Task A_rule_can_be_paused_and_deleted()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var service = BuildService(context);
        var created = await service.CreateAsync(
            user.Id, category.Id, 900_000, "Arriendo", 1, new DateOnly(2026, 9, 1));

        (await service.SetActiveAsync(user.Id, created.Rule!.Id, false)).Saved.Should().BeTrue();
        (await service.GetViewAsync(user.Id, created.Rule.Id))!.IsActive.Should().BeFalse();

        (await service.DeleteAsync(user.Id, created.Rule.Id)).Should().BeTrue();
        (await service.GetViewAsync(user.Id, created.Rule.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Applying_a_due_rule_creates_the_expense_once()
    {
        var clock = FixedTimeProvider.At(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Arriendo");
        context.Users.Add(user);
        context.Categories.Add(category);
        context.RecurringExpenses.Add(NewRule(user.Id, category.Id, dayOfMonth: 1));
        await context.SaveChangesAsync();

        var first = await BuildService(context, clock).ApplyDueAsync();

        first.Should().ContainSingle();
        first[0].Generated.Should().ContainSingle()
            .Which.Date.Should().Be(new DateOnly(2026, 9, 1));

        // A second run the same day must not duplicate the month's expense.
        await using var secondContext = CreateContext();
        var second = await BuildService(secondContext, clock).ApplyDueAsync();
        second.Should().BeEmpty();

        await using var verification = CreateContext();
        var items = await new ExpenseService(
                new ExpenseRepository(verification),
                new CategoryRepository(verification),
                SilentAlerts(),
                new UnitOfWork(verification))
            .ListMonthAsync(user.Id, new MonthPeriod(2026, 9));
        items.Should().ContainSingle().Which.Amount.Should().Be(900_000);
    }

    [Fact]
    public async Task The_pass_uses_the_users_calendar_date_not_utc()
    {
        // 02:00 UTC on 1 October is still 30 September in Bogotá. The rule day (1) must not
        // fire yet, or a user would see October's rent recorded on 30 September.
        var clock = FixedTimeProvider.At(new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero));
        await using var context = CreateContext();
        var user = TestData.NewUser();
        user.ChangeTimeZone("America/Bogota");
        var category = TestData.NewCategory(user.Id, "Arriendo");
        context.Users.Add(user);
        context.Categories.Add(category);
        context.RecurringExpenses.Add(NewRule(user.Id, category.Id, dayOfMonth: 1, startDate: new DateOnly(2026, 9, 1)));
        await context.SaveChangesAsync();

        var results = await BuildService(context, clock).ApplyDueAsync();

        // September's occurrence is the only one due; October's is still a day away locally.
        var generated = results.Should().ContainSingle().Subject.Generated;
        generated.Should().ContainSingle().Which.Date.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task A_paused_rule_is_never_applied()
    {
        var clock = FixedTimeProvider.At(new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        var rule = NewRule(user.Id, category.Id);
        rule.SetActive(false);
        context.RecurringExpenses.Add(rule);
        await context.SaveChangesAsync();

        (await BuildService(context, clock).ApplyDueAsync()).Should().BeEmpty();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public static FixedTimeProvider At(DateTimeOffset instant) => new(instant);

        public override DateTimeOffset GetUtcNow() => now;
    }
}
