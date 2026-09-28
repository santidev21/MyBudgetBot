using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Categories;
using MyBudget.Application.Localization;
using MyBudget.Domain.Categories;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The alias slice of the category flow. The interesting behaviour is the conflict prompt:
/// the same term may deliberately belong to two categories, so the prompt offers to store the
/// ambiguity rather than refusing it.
/// </summary>
public sealed class CategoriesConversationAliasTests
{
    private const long ChatId = 4242;

    private static ConversationContext ContextFor(
        TelegramHarness harness, string state, CategoriesPayload payload) =>
        new(
            harness.User,
            ChatId,
            new ConversationSnapshot(
                harness.User.Id,
                ChatId,
                CategoriesConversation.ConversationName,
                state,
                payload.Serialize(),
                TestClock.Now.AddMinutes(30)));

    private static TelegramHarness HarnessWith(BudgetCategory category)
    {
        var harness = TelegramHarness.Build();
        harness.CategoryService
            .GetAsync(Arg.Any<Guid>(), category.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BudgetCategory?>(category));
        harness.CategoryService
            .ListAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>([category]));
        return harness;
    }

    [Fact]
    public async Task The_alias_screen_lists_the_keywords_and_offers_add_and_back()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado", "🛒");
        category.AddAlias("mercado", "mercado");
        category.AddAlias("supermercado", "supermercado");
        harness.CategoryService
            .GetAsync(harness.User.Id, category.Id, Arg.Any<CancellationToken>())
            .Returns(category);

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new CategoriesPayload { CategoryId = category.Id }),
            new IncomingCallback("cb", "cats:aliases"),
            CancellationToken.None);

        turn.NextState.Should().Be("aliases");
        turn.Responses.Last().Text.Should().Contain(
            harness.Messages.Get("es", MessageKeys.AliasListHeader, "🛒 Mercado"));
        turn.Responses.Last().Keyboard!.Rows.Should().HaveCount(4, "two keywords, add, and back");
    }

    [Fact]
    public async Task A_category_without_keywords_says_so()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado", "🛒");
        harness.CategoryService
            .GetAsync(harness.User.Id, category.Id, Arg.Any<CancellationToken>())
            .Returns(category);

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new CategoriesPayload { CategoryId = category.Id }),
            new IncomingCallback("cb", "cats:aliases"),
            CancellationToken.None);

        turn.Responses.Last().Text.Should().Contain(
            harness.Messages.Get("es", MessageKeys.AliasListEmpty));
    }

    [Fact]
    public async Task Adding_a_keyword_stores_it_and_returns_to_the_screen()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado");
        var harness2 = HarnessWith(category);
        harness2.CategoryService
            .AddAliasAsync(Arg.Any<Guid>(), category.Id, "comida", false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AliasChangeResult.Added()));

        var turn = await harness2.Router.RouteTextAsync(
            ContextFor(harness2, "awaiting-alias", new CategoriesPayload { CategoryId = category.Id }),
            "comida",
            CancellationToken.None);

        await harness2.CategoryService.Received(1).AddAliasAsync(
            harness2.User.Id, category.Id, "comida", false, Arg.Any<CancellationToken>());
        turn.NextState.Should().Be("aliases");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.AliasAdded));
    }

    [Fact]
    public async Task A_keyword_already_in_the_category_reports_a_duplicate()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado");
        var harness2 = HarnessWith(category);
        harness2.CategoryService
            .AddAliasAsync(Arg.Any<Guid>(), category.Id, Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AliasChangeResult.Duplicate()));

        var turn = await harness2.Router.RouteTextAsync(
            ContextFor(harness2, "awaiting-alias", new CategoriesPayload { CategoryId = category.Id }),
            "mercado",
            CancellationToken.None);

        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.AliasDuplicate));
    }

    [Fact]
    public async Task A_too_long_keyword_is_rejected_without_calling_the_service()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado");
        var harness2 = HarnessWith(category);

        var turn = await harness2.Router.RouteTextAsync(
            ContextFor(harness2, "awaiting-alias", new CategoriesPayload { CategoryId = category.Id }),
            new string('a', BudgetCategory.MaxAliasLength + 1),
            CancellationToken.None);

        turn.NextState.Should().Be("awaiting-alias");
        turn.Responses[0].Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.AliasInvalid, BudgetCategory.MaxAliasLength));
        await harness2.CategoryService.DidNotReceiveWithAnyArgs().AddAliasAsync(
            default, default, default!, default, default);
    }

    [Fact]
    public async Task A_keyword_owned_elsewhere_asks_for_confirmation()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Restaurantes");
        var other = new BudgetCategory(harness.User.Id, "Mercado");
        var harness2 = HarnessWith(category);
        harness2.CategoryService
            .AddAliasAsync(Arg.Any<Guid>(), category.Id, "comida", false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AliasChangeResult.Conflict([other])));

        var turn = await harness2.Router.RouteTextAsync(
            ContextFor(harness2, "awaiting-alias", new CategoriesPayload { CategoryId = category.Id }),
            "comida",
            CancellationToken.None);

        turn.NextState.Should().Be("alias-conflict");
        turn.Responses[0].Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.AliasConflict, "Mercado"));
        turn.Responses[1].Keyboard!.Rows.Should().HaveCount(2, "add anyway and cancel");
    }

    [Fact]
    public async Task Confirming_the_conflict_adds_the_keyword_anyway()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Restaurantes");
        var harness2 = HarnessWith(category);
        harness2.CategoryService
            .AddAliasAsync(Arg.Any<Guid>(), category.Id, "comida", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AliasChangeResult.Added()));

        var turn = await harness2.Router.RouteCallbackAsync(
            ContextFor(
                harness2, "alias-conflict",
                new CategoriesPayload { CategoryId = category.Id, Alias = "comida" }),
            new IncomingCallback("cb", "cats:alias:confirm"),
            CancellationToken.None);

        await harness2.CategoryService.Received(1).AddAliasAsync(
            harness2.User.Id, category.Id, "comida", true, Arg.Any<CancellationToken>());
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.AliasAdded));
    }

    [Fact]
    public async Task Cancelling_the_conflict_does_not_add_the_keyword()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Restaurantes");
        var harness2 = HarnessWith(category);

        var turn = await harness2.Router.RouteCallbackAsync(
            ContextFor(
                harness2, "alias-conflict",
                new CategoriesPayload { CategoryId = category.Id, Alias = "comida" }),
            new IncomingCallback("cb", "cats:alias:cancel"),
            CancellationToken.None);

        turn.NextState.Should().Be("aliases");
        await harness2.CategoryService.DidNotReceiveWithAnyArgs().AddAliasAsync(
            default, default, default!, default, default);
    }

    [Fact]
    public async Task Removing_a_keyword_by_position_removes_that_term()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado");
        var harness2 = HarnessWith(category);
        harness2.CategoryService
            .RemoveAliasAsync(Arg.Any<Guid>(), category.Id, "comida", Arg.Any<CancellationToken>())
            .Returns(true);

        var turn = await harness2.Router.RouteCallbackAsync(
            ContextFor(
                harness2, "aliases",
                new CategoriesPayload
                {
                    CategoryId = category.Id,
                    Aliases = ["mercado", "comida"],
                }),
            new IncomingCallback("cb", "cats:alias:rm:1"),
            CancellationToken.None);

        await harness2.CategoryService.Received(1).RemoveAliasAsync(
            harness2.User.Id, category.Id, "comida", Arg.Any<CancellationToken>());
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.AliasRemoved));
    }
}
