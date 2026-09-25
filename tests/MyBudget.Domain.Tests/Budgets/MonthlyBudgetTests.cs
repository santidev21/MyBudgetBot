using FluentAssertions;
using MyBudget.Domain.Budgets;

namespace MyBudget.Domain.Tests.Budgets;

public sealed class MonthlyBudgetTests
{
    private static readonly MonthPeriod September = new(2026, 9);

    [Fact]
    public void SetAllocation_creates_a_line_the_first_time()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var budget = new MonthlyBudget(userId, September);

        var allocation = budget.SetAllocation(categoryId, 1_000_000);

        budget.Allocations.Should().HaveCount(1);
        allocation.Amount.Should().Be(1_000_000);
        allocation.UserId.Should().Be(userId);
        budget.TotalAllocated.Should().Be(1_000_000);
    }

    [Fact]
    public void SetAllocation_updates_the_existing_line_instead_of_duplicating_it()
    {
        var categoryId = Guid.NewGuid();
        var budget = new MonthlyBudget(Guid.NewGuid(), September);

        budget.SetAllocation(categoryId, 1_000_000);
        budget.SetAllocation(categoryId, 1_500_000);

        budget.Allocations.Should().HaveCount(1);
        budget.TotalAllocated.Should().Be(1_500_000);
    }

    [Fact]
    public void A_zero_allocation_is_valid()
    {
        var budget = new MonthlyBudget(Guid.NewGuid(), September);

        budget.SetAllocation(Guid.NewGuid(), 0);

        budget.TotalAllocated.Should().Be(0);
    }

    [Fact]
    public void A_negative_allocation_is_rejected()
    {
        var budget = new MonthlyBudget(Guid.NewGuid(), September);

        FluentActions.Invoking(() => budget.SetAllocation(Guid.NewGuid(), -1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RemoveAllocation_removes_only_the_requested_line()
    {
        var keep = Guid.NewGuid();
        var drop = Guid.NewGuid();
        var budget = new MonthlyBudget(Guid.NewGuid(), September);
        budget.SetAllocation(keep, 100_000);
        budget.SetAllocation(drop, 200_000);

        var removed = budget.RemoveAllocation(drop);

        removed.Should().BeTrue();
        budget.Allocations.Should().ContainSingle(a => a.CategoryId == keep);
        budget.TotalAllocated.Should().Be(100_000);
    }

    [Fact]
    public void RemoveAllocation_reports_false_for_an_unknown_category()
    {
        var budget = new MonthlyBudget(Guid.NewGuid(), September);

        budget.RemoveAllocation(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void CopyAllocationsFrom_replicates_the_previous_month()
    {
        var userId = Guid.NewGuid();
        var (market, housing) = (Guid.NewGuid(), Guid.NewGuid());
        var september = new MonthlyBudget(userId, September);
        september.SetAllocation(market, 1_000_000);
        september.SetAllocation(housing, 1_800_000);

        var october = new MonthlyBudget(userId, September.Next);
        october.CopyAllocationsFrom(september);

        october.TotalAllocated.Should().Be(2_800_000);
        october.Allocations.Should().HaveCount(2);
    }

    [Fact]
    public void CopyAllocationsFrom_does_not_link_the_two_months()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var september = new MonthlyBudget(userId, September);
        september.SetAllocation(categoryId, 1_000_000);

        var october = new MonthlyBudget(userId, September.Next);
        october.CopyAllocationsFrom(september);

        // Editing October must never rewrite September: the historical snapshot is a copy.
        october.SetAllocation(categoryId, 1_500_000);

        september.Allocations.Single().Amount.Should().Be(1_000_000);
        october.Allocations.Single().Amount.Should().Be(1_500_000);
    }

    [Fact]
    public void The_period_is_reconstructed_from_the_stored_year_and_month()
    {
        var budget = new MonthlyBudget(Guid.NewGuid(), September);

        budget.Period.Should().Be(September);
    }
}
