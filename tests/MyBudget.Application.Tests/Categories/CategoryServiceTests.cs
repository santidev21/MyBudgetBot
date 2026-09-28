using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Categories;
using MyBudget.Domain.Categories;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace MyBudget.Application.Tests.Categories;

/// <summary>
/// The category use cases against substituted repositories.
/// <para>
/// These tests pin the behaviour the Telegram flows depend on: reuse of a name reactivates
/// rather than duplicates, a term already owned elsewhere is a conflict that can still be
/// accepted on purpose, and deletion is simply not part of the surface.
/// </para>
/// </summary>
public sealed class CategoryServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CategoryService _service;

    public CategoryServiceTests()
    {
        _categories
            .FindByAliasAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BudgetCategory>());

        _service = new CategoryService(_categories, _unitOfWork);
    }

    [Fact]
    public async Task Creating_a_category_with_a_free_name_saves_it()
    {
        _categories.FindByNameAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((BudgetCategory?)null);

        var result = await _service.CreateAsync(UserId, "Mercado", "🛒");

        result.Status.Should().Be(CategoryChangeStatus.Saved);
        result.Category!.Name.Should().Be("Mercado");
        result.Category.Icon.Should().Be("🛒");
        _categories.Received(1).Add(result.Category);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_a_name_an_active_category_already_uses_is_refused()
    {
        var existing = new BudgetCategory(UserId, "Mercado");
        _categories.FindByNameAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _service.CreateAsync(UserId, "mercado", null);

        result.Status.Should().Be(CategoryChangeStatus.NameTaken);
        _categories.DidNotReceive().Add(Arg.Any<BudgetCategory>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_a_name_a_deactivated_category_used_reactivates_it()
    {
        var existing = new BudgetCategory(UserId, "Mercado");
        existing.Deactivate();
        _categories.FindByNameAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _service.CreateAsync(UserId, "Mercado", "🛍️");

        result.Status.Should().Be(CategoryChangeStatus.Reactivated);
        result.Category.Should().BeSameAs(existing);
        existing.IsActive.Should().BeTrue();
        existing.Icon.Should().Be("🛍️");
        _categories.DidNotReceive().Add(Arg.Any<BudgetCategory>());
    }

    [Fact]
    public async Task A_unique_violation_during_create_resolves_against_the_winner()
    {
        var active = new BudgetCategory(UserId, "Mercado");
        var calls = 0;
        _categories.FindByNameAsync(UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<BudgetCategory?>(calls++ == 0 ? null : active));
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintViolationException("uq_categories_user_name", new InvalidOperationException()));

        var result = await _service.CreateAsync(UserId, "Mercado", null);

        result.Status.Should().Be(CategoryChangeStatus.NameTaken);
        _unitOfWork.Received(1).Detach(Arg.Any<object>());
    }

    [Fact]
    public async Task Renaming_onto_another_categorys_name_is_refused()
    {
        var category = new BudgetCategory(UserId, "Transporte");
        var other = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _categories.FindByNameAsync(UserId, "Mercado", Arg.Any<CancellationToken>()).Returns(other);

        var result = await _service.RenameAsync(UserId, category.Id, "Mercado");

        result.Status.Should().Be(CategoryChangeStatus.NameTaken);
        category.Name.Should().Be("Transporte");
    }

    [Fact]
    public async Task Renaming_a_category_to_its_own_name_is_allowed()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _categories.FindByNameAsync(UserId, "Mercado", Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.RenameAsync(UserId, category.Id, "Mercado");

        result.Status.Should().Be(CategoryChangeStatus.Saved);
    }

    [Fact]
    public async Task Changing_a_missing_category_reports_not_found()
    {
        _categories.FindByIdAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((BudgetCategory?)null);

        var result = await _service.SetActiveAsync(UserId, Guid.NewGuid(), isActive: false);

        result.Status.Should().Be(CategoryChangeStatus.NotFound);
    }

    [Fact]
    public async Task Deactivating_and_reactivating_flips_the_flag()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        await _service.SetActiveAsync(UserId, category.Id, isActive: false);
        category.IsActive.Should().BeFalse();

        await _service.SetActiveAsync(UserId, category.Id, isActive: true);
        category.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Adding_a_keyword_stores_the_normalized_form()
    {
        var category = new BudgetCategory(UserId, "Café");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.AddAliasAsync(UserId, category.Id, "  Café  ", allowConflict: false);

        result.Status.Should().Be(AliasChangeStatus.Added);
        category.Aliases.Should().ContainSingle()
            .Which.NormalizedAlias.Should().Be("cafe");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_keyword_already_in_the_same_category_is_a_duplicate()
    {
        var category = new BudgetCategory(UserId, "Café");
        category.AddAlias("cafe", "cafe");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.AddAliasAsync(UserId, category.Id, "CAFÉ", allowConflict: false);

        result.Status.Should().Be(AliasChangeStatus.Duplicate);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_keyword_owned_by_another_category_is_reported_as_a_conflict()
    {
        var category = new BudgetCategory(UserId, "Restaurantes");
        var other = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _categories.FindByAliasAsync(UserId, "comida", Arg.Any<CancellationToken>())
            .Returns([other]);

        var result = await _service.AddAliasAsync(UserId, category.Id, "comida", allowConflict: false);

        result.Status.Should().Be(AliasChangeStatus.Conflict);
        result.ConflictingCategories.Should().ContainSingle().Which.Should().BeSameAs(other);
        category.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task A_conflicting_keyword_can_still_be_added_on_purpose()
    {
        var category = new BudgetCategory(UserId, "Restaurantes");
        var other = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _categories.FindByAliasAsync(UserId, "comida", Arg.Any<CancellationToken>())
            .Returns([other]);

        var result = await _service.AddAliasAsync(UserId, category.Id, "comida", allowConflict: true);

        result.Status.Should().Be(AliasChangeStatus.Added);
        category.Aliases.Should().ContainSingle();
    }

    [Fact]
    public async Task A_keyword_is_removed_by_its_term()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        category.AddAlias("verduras", "verduras");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var removed = await _service.RemoveAliasAsync(UserId, category.Id, "  VERDURAS ");

        removed.Should().BeTrue();
        category.Aliases.Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_an_unknown_keyword_reports_false()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var removed = await _service.RemoveAliasAsync(UserId, category.Id, "nada");

        removed.Should().BeFalse();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Café", "cafe")]
    [InlineData("  Pago   de   servicios ", "pago de servicios")]
    [InlineData("PAGUÉ", "pague")]
    public void Normalization_folds_case_accents_and_whitespace(string input, string expected) =>
        CategoryAliasNormalizer.Normalize(input).Should().Be(expected);
}
