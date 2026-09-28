using FluentAssertions;
using MyBudget.Application.Categories;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Category management through the real service and repositories against PostgreSQL.
/// <para>
/// The unit tests use substitutes; these prove that the same use cases actually persist, that
/// the unique index is the arbiter of a name clash, and that a shared keyword is stored on two
/// categories on purpose rather than being blocked by the database.
/// </para>
/// </summary>
public sealed class CategoryManagementTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Creating_a_category_persists_its_trimmed_name_and_icon()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new CategoryService(new CategoryRepository(context), new UnitOfWork(context));

        var result = await service.CreateAsync(user.Id, "  Mercado  ", "🛒");

        result.Succeeded.Should().BeTrue();

        await using var verification = CreateContext();
        var stored = await new CategoryRepository(verification).FindByNameAsync(user.Id, "mercado");
        stored.Should().NotBeNull();
        stored!.Name.Should().Be("Mercado");
        stored.Icon.Should().Be("🛒");
    }

    [Fact]
    public async Task A_duplicate_name_is_refused_by_the_real_unique_index()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new CategoryService(new CategoryRepository(context), new UnitOfWork(context));
        await service.CreateAsync(user.Id, "Mercado", null);

        var second = await service.CreateAsync(user.Id, "mercado", null);

        second.Status.Should().Be(CategoryChangeStatus.NameTaken);

        await using var verification = CreateContext();
        (await new CategoryRepository(verification).ListAsync(user.Id, includeInactive: true))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Reusing_a_deactivated_name_reactivates_the_same_row()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new CategoryService(new CategoryRepository(context), new UnitOfWork(context));
        var first = await service.CreateAsync(user.Id, "Mercado", "🛒");
        await service.SetActiveAsync(user.Id, first.Category!.Id, isActive: false);

        var again = await service.CreateAsync(user.Id, "Mercado", "🛍️");

        again.Status.Should().Be(CategoryChangeStatus.Reactivated);
        again.Category!.Id.Should().Be(first.Category.Id);
        again.Category.IsActive.Should().BeTrue();

        await using var verification = CreateContext();
        (await new CategoryRepository(verification).ListAsync(user.Id, includeInactive: true))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task An_alias_is_persisted_with_its_normalized_form()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new CategoryService(new CategoryRepository(context), new UnitOfWork(context));
        var created = await service.CreateAsync(user.Id, "Café", null);

        var added = await service.AddAliasAsync(user.Id, created.Category!.Id, "  Café  ", allowConflict: false);

        added.Status.Should().Be(AliasChangeStatus.Added);

        await using var verification = CreateContext();
        var stored = await new CategoryRepository(verification).FindByIdAsync(user.Id, created.Category.Id);
        stored!.Aliases.Should().ContainSingle().Which.NormalizedAlias.Should().Be("cafe");
    }

    [Fact]
    public async Task A_keyword_shared_between_categories_is_stored_on_both()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new CategoryService(new CategoryRepository(context), new UnitOfWork(context));
        var restaurants = await service.CreateAsync(user.Id, "Restaurantes", null);
        var market = await service.CreateAsync(user.Id, "Mercado", null);

        await service.AddAliasAsync(user.Id, restaurants.Category!.Id, "comida", allowConflict: false);

        var conflict = await service.AddAliasAsync(user.Id, market.Category!.Id, "comida", allowConflict: false);
        conflict.Status.Should().Be(AliasChangeStatus.Conflict);
        conflict.ConflictingCategories.Should().ContainSingle()
            .Which.Id.Should().Be(restaurants.Category.Id);

        var forced = await service.AddAliasAsync(user.Id, market.Category!.Id, "comida", allowConflict: true);
        forced.Status.Should().Be(AliasChangeStatus.Added);

        await using var verification = CreateContext();
        var owners = await new CategoryRepository(verification).FindByAliasAsync(user.Id, "comida");
        owners.Should().HaveCount(2, "ambiguity is modelled in data, not prevented");
    }

    [Fact]
    public async Task Removing_a_keyword_deletes_its_row()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = new CategoryService(new CategoryRepository(context), new UnitOfWork(context));
        var created = await service.CreateAsync(user.Id, "Mercado", null);
        await service.AddAliasAsync(user.Id, created.Category!.Id, "verduras", allowConflict: false);

        var removed = await service.RemoveAliasAsync(user.Id, created.Category.Id, "VERDURAS");

        removed.Should().BeTrue();

        await using var verification = CreateContext();
        var stored = await new CategoryRepository(verification).FindByIdAsync(user.Id, created.Category.Id);
        stored!.Aliases.Should().BeEmpty();
    }
}
