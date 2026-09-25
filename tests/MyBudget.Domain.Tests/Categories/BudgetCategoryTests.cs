using FluentAssertions;
using MyBudget.Domain.Categories;

namespace MyBudget.Domain.Tests.Categories;

public sealed class BudgetCategoryTests
{
    [Fact]
    public void A_category_is_active_by_default()
    {
        var category = new BudgetCategory(Guid.NewGuid(), "Mercado", "🛒");

        category.IsActive.Should().BeTrue();
        category.Name.Should().Be("Mercado");
        category.Icon.Should().Be("🛒");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_rejected(string? name)
    {
        FluentActions.Invoking(() => new BudgetCategory(Guid.NewGuid(), name!))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_rejected()
    {
        var tooLong = new string('a', BudgetCategory.MaxNameLength + 1);

        FluentActions.Invoking(() => new BudgetCategory(Guid.NewGuid(), tooLong))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_name_is_trimmed()
    {
        new BudgetCategory(Guid.NewGuid(), "  Mercado  ").Name.Should().Be("Mercado");
    }

    [Fact]
    public void The_icon_falls_back_to_the_default_when_missing()
    {
        new BudgetCategory(Guid.NewGuid(), "Mercado").Icon.Should().Be(BudgetCategory.DefaultIcon);
        new BudgetCategory(Guid.NewGuid(), "Mercado", "  ").Icon.Should().Be(BudgetCategory.DefaultIcon);
    }

    [Fact]
    public void An_empty_user_id_is_rejected()
    {
        FluentActions.Invoking(() => new BudgetCategory(Guid.Empty, "Mercado"))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deactivate_and_activate_toggle_visibility_without_deleting()
    {
        var category = new BudgetCategory(Guid.NewGuid(), "Mercado");

        category.Deactivate();
        category.IsActive.Should().BeFalse();

        category.Activate();
        category.IsActive.Should().BeTrue();
    }

    [Fact]
    public void An_alias_can_be_added_and_removed()
    {
        var category = new BudgetCategory(Guid.NewGuid(), "Mercado");

        var alias = category.AddAlias("Verduras", "verduras");

        category.Aliases.Should().ContainSingle();
        alias.NormalizedAlias.Should().Be("verduras");
        alias.CategoryId.Should().Be(category.Id);
        alias.UserId.Should().Be(category.UserId);

        category.RemoveAlias(alias.Id);
        category.Aliases.Should().BeEmpty();
    }

    [Fact]
    public void The_same_normalized_alias_cannot_be_added_twice_to_one_category()
    {
        var category = new BudgetCategory(Guid.NewGuid(), "Mercado");
        category.AddAlias("Verduras", "verduras");

        FluentActions.Invoking(() => category.AddAlias("VERDURAS", "verduras"))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Removing_an_unknown_alias_is_a_no_op()
    {
        var category = new BudgetCategory(Guid.NewGuid(), "Mercado");
        category.AddAlias("Verduras", "verduras");

        category.RemoveAlias(Guid.NewGuid());

        category.Aliases.Should().ContainSingle();
    }
}
