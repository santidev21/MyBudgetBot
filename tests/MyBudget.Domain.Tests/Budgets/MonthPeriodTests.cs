using FluentAssertions;
using MyBudget.Domain.Budgets;

namespace MyBudget.Domain.Tests.Budgets;

public sealed class MonthPeriodTests
{
    [Theory]
    [InlineData(2026, 1, 2025, 12)]
    [InlineData(2026, 9, 2026, 8)]
    [InlineData(2026, 12, 2026, 11)]
    public void Previous_returns_the_preceding_month(
        int year, int month, int expectedYear, int expectedMonth)
    {
        var period = new MonthPeriod(year, month);

        period.Previous.Should().Be(new MonthPeriod(expectedYear, expectedMonth));
    }

    [Theory]
    [InlineData(2025, 12, 2026, 1)]
    [InlineData(2026, 9, 2026, 10)]
    [InlineData(2026, 1, 2026, 2)]
    public void Next_returns_the_following_month(
        int year, int month, int expectedYear, int expectedMonth)
    {
        var period = new MonthPeriod(year, month);

        period.Next.Should().Be(new MonthPeriod(expectedYear, expectedMonth));
    }

    [Fact]
    public void FromDate_derives_the_period_from_a_calendar_date()
    {
        MonthPeriod.FromDate(new DateOnly(2026, 9, 25)).Should().Be(new MonthPeriod(2026, 9));
    }

    [Theory]
    [InlineData(2026, 9, true)]
    [InlineData(2026, 10, false)]
    [InlineData(2025, 9, false)]
    public void Contains_only_accepts_dates_of_the_same_month(int year, int month, bool expected)
    {
        var period = new MonthPeriod(2026, 9);

        period.Contains(new DateOnly(year, month, 15)).Should().Be(expected);
    }

    [Theory]
    [InlineData(2026, 9, 1)]
    [InlineData(2026, 2, 1)]
    [InlineData(2024, 2, 1)]
    public void FirstDay_is_always_the_first_of_the_month(int year, int month, int expectedDay)
    {
        new MonthPeriod(year, month).FirstDay.Should().Be(new DateOnly(year, month, expectedDay));
    }

    [Theory]
    [InlineData(2024, 2, 29)]
    [InlineData(2025, 2, 28)]
    [InlineData(2026, 9, 30)]
    [InlineData(2026, 4, 30)]
    public void LastDay_respects_month_length_and_leap_years(int year, int month, int expectedDay)
    {
        new MonthPeriod(year, month).LastDay.Should().Be(new DateOnly(year, month, expectedDay));
    }

    [Theory]
    [InlineData(1999, 1)]
    [InlineData(2101, 1)]
    public void Year_out_of_range_is_rejected(int year, int month)
    {
        FluentActions.Invoking(() => new MonthPeriod(year, month))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(2026, 0)]
    [InlineData(2026, 13)]
    public void Month_out_of_range_is_rejected(int year, int month)
    {
        FluentActions.Invoking(() => new MonthPeriod(year, month))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToString_is_stable_for_logs_and_callback_data()
    {
        new MonthPeriod(2026, 9).ToString().Should().Be("2026-09");
    }

    [Fact]
    public void Previous_is_not_defined_before_the_supported_range()
    {
        // Navigation is bounded by the interface, so walking off the edge of the supported
        // range is a programming error and must be loud rather than silently clamped.
        FluentActions.Invoking(() => new MonthPeriod(MonthPeriod.MinYear, 1).Previous)
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Next_is_not_defined_after_the_supported_range()
    {
        FluentActions.Invoking(() => new MonthPeriod(MonthPeriod.MaxYear, 12).Next)
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_first_and_last_supported_periods_are_valid()
    {
        var first = new MonthPeriod(MonthPeriod.MinYear, 1);
        var last = new MonthPeriod(MonthPeriod.MaxYear, 12);

        first.FirstDay.Should().Be(new DateOnly(MonthPeriod.MinYear, 1, 1));
        last.LastDay.Should().Be(new DateOnly(MonthPeriod.MaxYear, 12, 31));
    }
}
