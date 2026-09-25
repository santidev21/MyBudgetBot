using FluentAssertions;
using MyBudget.Domain.Budgets;

namespace MyBudget.Domain.Tests.Budgets;

public sealed class BudgetMathTests
{
    [Theory]
    [InlineData(1_000_000, 720_000, 72.0)]
    [InlineData(1_000_000, 1_150_000, 115.0)]
    [InlineData(1_800_000, 900_000, 50.0)]
    [InlineData(1_000_000, 0, 0.0)]
    public void UsagePercentage_is_the_share_of_the_allocation(long budget, long spent, double expected)
    {
        BudgetMath.UsagePercentage(budget, spent).Should().Be((decimal)expected);
    }

    [Theory]
    [InlineData(0, 5_000)]
    [InlineData(0, 0)]
    [InlineData(-1, 5_000)]
    public void UsagePercentage_is_undefined_without_a_budget(long budget, long spent)
    {
        BudgetMath.UsagePercentage(budget, spent).Should().BeNull();
    }

    [Theory]
    [InlineData(3_000, 1_000, 33.3)]
    [InlineData(3_000, 2_000, 66.7)]
    [InlineData(7_000, 1_000, 14.3)]
    public void UsagePercentage_rounds_to_one_decimal(long budget, long spent, double expected)
    {
        BudgetMath.UsagePercentage(budget, spent).Should().Be((decimal)expected);
    }

    [Fact]
    public void Totals_are_derived_from_the_lines()
    {
        var lines = new[]
        {
            new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 1_000_000, 720_000),
            new BudgetLine(Guid.NewGuid(), "Vivienda", "🏠", 1_800_000, 900_000),
            new BudgetLine(Guid.NewGuid(), "Transporte", "⛽", 230_000, 260_000),
        };

        BudgetMath.TotalBudget(lines).Should().Be(3_030_000);
        BudgetMath.TotalSpent(lines).Should().Be(1_880_000);
        BudgetMath.TotalRemaining(lines).Should().Be(1_150_000);
    }

    [Fact]
    public void Overall_usage_percentage_uses_the_totals()
    {
        var lines = new[]
        {
            new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 2_000_000, 1_000_000),
            new BudgetLine(Guid.NewGuid(), "Vivienda", "🏠", 2_000_000, 500_000),
        };

        BudgetMath.OverallUsagePercentage(lines).Should().Be(37.5m);
    }

    [Fact]
    public void Overall_usage_percentage_is_undefined_when_nothing_is_allocated()
    {
        var lines = new[]
        {
            new BudgetLine(Guid.NewGuid(), "Otros", "📦", 0, 5_000),
        };

        BudgetMath.OverallUsagePercentage(lines).Should().BeNull();
    }

    [Fact]
    public void Totals_of_an_empty_month_are_zero()
    {
        var lines = Array.Empty<BudgetLine>();

        BudgetMath.TotalBudget(lines).Should().Be(0);
        BudgetMath.TotalSpent(lines).Should().Be(0);
        BudgetMath.TotalRemaining(lines).Should().Be(0);
        BudgetMath.OverallUsagePercentage(lines).Should().BeNull();
    }

    [Fact]
    public void A_line_within_budget_reports_the_remaining_amount()
    {
        var line = new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 1_000_000, 720_000);

        line.HasBudget.Should().BeTrue();
        line.IsOverspent.Should().BeFalse();
        line.Remaining.Should().Be(280_000);
        line.Overage.Should().Be(0);
    }

    [Fact]
    public void An_overspent_line_reports_the_excess_and_a_negative_remaining()
    {
        var line = new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 1_000_000, 1_150_000);

        line.IsOverspent.Should().BeTrue();
        line.Remaining.Should().Be(-150_000);
        line.Overage.Should().Be(150_000);
        line.UsagePercentage.Should().Be(115.0m);
    }

    [Fact]
    public void A_line_without_allocation_is_not_overspent_when_nothing_was_spent()
    {
        var line = new BudgetLine(Guid.NewGuid(), "Otros", "📦", 0, 0);

        line.HasBudget.Should().BeFalse();
        line.IsOverspent.Should().BeFalse();
        line.Remaining.Should().Be(0);
        line.UsagePercentage.Should().BeNull();
    }
}
