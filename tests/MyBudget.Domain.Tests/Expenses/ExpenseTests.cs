using FluentAssertions;
using MyBudget.Domain.Expenses;

namespace MyBudget.Domain.Tests.Expenses;

public sealed class ExpenseTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static Expense CreateExpense(
        long amount = 35_000,
        string? description = "Verduras",
        DateOnly? date = null,
        CategorizationSource source = CategorizationSource.Manual)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            amount,
            description,
            date ?? Today,
            Today,
            source);

    [Fact]
    public void A_valid_expense_is_created()
    {
        var expense = CreateExpense();

        expense.Amount.Should().Be(35_000);
        expense.Description.Should().Be("Verduras");
        expense.ExpenseDate.Should().Be(Today);
        expense.CategorizationSource.Should().Be(CategorizationSource.Manual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-35_000)]
    public void A_non_positive_amount_is_rejected(long amount)
    {
        FluentActions.Invoking(() => CreateExpense(amount: amount))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void An_amount_above_the_upper_bound_is_rejected()
    {
        FluentActions.Invoking(() => CreateExpense(amount: Expense.MaxAmount + 1))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_upper_bound_itself_is_accepted()
    {
        CreateExpense(amount: Expense.MaxAmount).Amount.Should().Be(Expense.MaxAmount);
    }

    [Fact]
    public void A_future_date_is_rejected()
    {
        FluentActions.Invoking(() => CreateExpense(date: Today.AddDays(1)))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Today_is_accepted()
    {
        CreateExpense(date: Today).ExpenseDate.Should().Be(Today);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_description_becomes_null(string? description)
    {
        CreateExpense(description: description).Description.Should().BeNull();
    }

    [Fact]
    public void A_description_is_trimmed()
    {
        CreateExpense(description: "  Verduras y frutas  ").Description.Should().Be("Verduras y frutas");
    }

    [Fact]
    public void A_description_longer_than_the_limit_is_rejected()
    {
        var tooLong = new string('a', Expense.MaxDescriptionLength + 1);

        FluentActions.Invoking(() => CreateExpense(description: tooLong))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangeAmount_rejects_a_non_positive_value_and_keeps_the_previous_one()
    {
        var expense = CreateExpense(amount: 35_000);

        FluentActions.Invoking(() => expense.ChangeAmount(0))
            .Should().Throw<ArgumentOutOfRangeException>();

        expense.Amount.Should().Be(35_000);
    }

    [Fact]
    public void ChangeDate_rejects_a_future_date_and_keeps_the_previous_one()
    {
        var expense = CreateExpense(date: Today);

        FluentActions.Invoking(() => expense.ChangeDate(Today.AddDays(1), Today))
            .Should().Throw<ArgumentException>();

        expense.ExpenseDate.Should().Be(Today);
    }

    [Fact]
    public void An_expense_can_be_moved_to_an_earlier_date()
    {
        var expense = CreateExpense(date: Today);

        expense.ChangeDate(Today.AddDays(-30), Today);

        expense.ExpenseDate.Should().Be(Today.AddDays(-30));
    }

    [Fact]
    public void ChangeCategory_records_how_the_category_was_chosen()
    {
        var expense = CreateExpense();
        var newCategoryId = Guid.NewGuid();

        expense.ChangeCategory(newCategoryId, CategorizationSource.Learned);

        expense.CategoryId.Should().Be(newCategoryId);
        expense.CategorizationSource.Should().Be(CategorizationSource.Learned);
    }

    [Fact]
    public void An_empty_category_id_is_rejected()
    {
        FluentActions.Invoking(() => CreateExpense().ChangeCategory(Guid.Empty))
            .Should().Throw<ArgumentException>();
    }
}
