using FluentAssertions;
using MyBudget.Domain.Common;
using MyBudget.Domain.Recurring;

namespace MyBudget.Domain.Tests.Recurring;

public sealed class RecurringExpenseTests
{
    private static RecurringExpense Create(
        long amount = 900_000,
        string? description = "Arriendo",
        int dayOfMonth = 1,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            amount,
            description,
            dayOfMonth,
            startDate ?? new DateOnly(2026, 1, 1),
            endDate);

    [Fact]
    public void A_valid_rule_starts_active_and_has_no_history()
    {
        var rule = Create();

        rule.Amount.Should().Be(900_000);
        rule.Description.Should().Be("Arriendo");
        rule.DayOfMonth.Should().Be(1);
        rule.IsActive.Should().BeTrue();
        rule.LastGeneratedDate.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_amount_is_rejected(long amount)
    {
        FluentActions.Invoking(() => Create(amount: amount))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_amount_above_the_upper_bound_is_rejected()
    {
        FluentActions.Invoking(() => Create(amount: MoneyLimits.MaxAmount + 1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(-5)]
    public void A_day_outside_the_month_is_rejected(int day) =>
        FluentActions.Invoking(() => Create(dayOfMonth: day))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void An_end_before_the_start_is_rejected() =>
        FluentActions.Invoking(() => Create(
                startDate: new DateOnly(2026, 3, 1),
                endDate: new DateOnly(2026, 2, 1)))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void An_occurrence_before_the_start_date_in_the_same_month_is_skipped()
    {
        var rule = Create(dayOfMonth: 5, startDate: new DateOnly(2026, 3, 10));

        rule.DueDates(new DateOnly(2026, 3, 20)).Should().BeEmpty();
        rule.DueDates(new DateOnly(2026, 4, 5)).Should().Equal(new DateOnly(2026, 4, 5));
    }

    [Fact]
    public void An_occurrence_on_or_before_today_is_due()
    {
        var rule = Create(dayOfMonth: 15, startDate: new DateOnly(2026, 3, 1));

        rule.DueDates(new DateOnly(2026, 3, 20)).Should().Equal(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void A_shorter_month_clamps_to_its_last_day()
    {
        // 31 February is not a date; the rule must still fall due.
        var rule = Create(dayOfMonth: 31, startDate: new DateOnly(2026, 2, 1));

        rule.DueDates(new DateOnly(2026, 2, 28)).Should().Equal(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void Every_missed_month_is_caught_up_oldest_first()
    {
        var rule = Create(dayOfMonth: 1, startDate: new DateOnly(2026, 1, 1));

        rule.DueDates(new DateOnly(2026, 4, 15)).Should().Equal(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 4, 1));
    }

    [Fact]
    public void A_run_never_returns_more_than_the_cap()
    {
        var rule = Create(dayOfMonth: 1, startDate: new DateOnly(2020, 1, 1));

        rule.DueDates(new DateOnly(2026, 12, 31), maxOccurrences: 3).Should().Equal(
            new DateOnly(2020, 1, 1),
            new DateOnly(2020, 2, 1),
            new DateOnly(2020, 3, 1));
    }

    [Fact]
    public void An_inactive_rule_is_never_due()
    {
        var rule = Create();
        rule.SetActive(false);

        rule.DueDates(new DateOnly(2026, 12, 31)).Should().BeEmpty();
    }

    [Fact]
    public void The_end_date_is_inclusive()
    {
        var rule = Create(
            dayOfMonth: 1,
            startDate: new DateOnly(2026, 1, 1),
            endDate: new DateOnly(2026, 2, 1));

        rule.DueDates(new DateOnly(2026, 6, 30)).Should().Equal(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1));
    }

    [Fact]
    public void Recording_a_generation_removes_that_occurrence_from_the_next_run()
    {
        var rule = Create(dayOfMonth: 1, startDate: new DateOnly(2026, 1, 1));

        rule.RecordGeneration(new DateOnly(2026, 1, 1));

        rule.LastGeneratedDate.Should().Be(new DateOnly(2026, 1, 1));
        rule.DueDates(new DateOnly(2026, 3, 15)).Should().Equal(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Recording_a_generation_must_move_forward()
    {
        var rule = Create();
        rule.RecordGeneration(new DateOnly(2026, 2, 1));

        FluentActions.Invoking(() => rule.RecordGeneration(new DateOnly(2026, 2, 1)))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => rule.RecordGeneration(new DateOnly(2026, 1, 1)))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_description_is_trimmed_or_null()
    {
        var rule = Create(description: "   ");

        rule.Description.Should().BeNull();
        rule.ChangeDescription("  Luz  ");
        rule.Description.Should().Be("Luz");
    }
}
