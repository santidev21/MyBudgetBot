using FluentAssertions;
using MyBudget.Domain.Budgets;

namespace MyBudget.Domain.Tests.Budgets;

public sealed class BudgetDefaultTests
{
    private static readonly MonthPeriod September = new(2026, 9);

    [Fact]
    public void A_default_carries_its_amount_and_the_month_it_starts()
    {
        var userId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var budgetDefault = new BudgetDefault(userId, categoryId, September, 1_000_000);

        budgetDefault.UserId.Should().Be(userId);
        budgetDefault.CategoryId.Should().Be(categoryId);
        budgetDefault.Amount.Should().Be(1_000_000);
        budgetDefault.EffectiveFrom.Should().Be(September);
    }

    [Fact]
    public void It_applies_from_its_month_onwards_but_not_before()
    {
        var budgetDefault = new BudgetDefault(Guid.NewGuid(), Guid.NewGuid(), September, 1_000_000);

        budgetDefault.AppliesTo(September).Should().BeTrue();
        budgetDefault.AppliesTo(September.Next).Should().BeTrue();
        budgetDefault.AppliesTo(September.Previous).Should().BeFalse();
    }

    [Fact]
    public void Changing_the_amount_keeps_the_month_it_started()
    {
        var budgetDefault = new BudgetDefault(Guid.NewGuid(), Guid.NewGuid(), September, 1_000_000);

        budgetDefault.ChangeAmount(1_500_000);

        budgetDefault.Amount.Should().Be(1_500_000);
        budgetDefault.EffectiveFrom.Should().Be(September);
    }

    [Fact]
    public void A_negative_amount_is_rejected()
    {
        FluentActions.Invoking(() => new BudgetDefault(Guid.NewGuid(), Guid.NewGuid(), September, -1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_default_needs_a_user_and_a_category()
    {
        FluentActions.Invoking(() => new BudgetDefault(Guid.Empty, Guid.NewGuid(), September, 1))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => new BudgetDefault(Guid.NewGuid(), Guid.Empty, September, 1))
            .Should().Throw<ArgumentException>();
    }
}
