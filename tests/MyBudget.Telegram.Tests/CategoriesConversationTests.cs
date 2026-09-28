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
/// The category flow, driven through the real router.
/// <para>
/// The service is substituted, so what is under test is the conversation: which state each
/// input produces, which service call it triggers, and what the user is told. The persistence
/// rules themselves are covered by the service and infrastructure tests.
/// </para>
/// </summary>
public sealed class CategoriesConversationTests
{
    private const long ChatId = 4242;

    private static ConversationContext ContextFor(
        TelegramHarness harness, string? state = null, CategoriesPayload? payload = null) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new ConversationSnapshot(
                    harness.User.Id,
                    ChatId,
                    CategoriesConversation.ConversationName,
                    state,
                    (payload ?? new CategoriesPayload()).Serialize(),
                    TestClock.Now.AddMinutes(30)));

    private static TelegramHarness HarnessWith(params BudgetCategory[] categories)
    {
        var harness = TelegramHarness.Build();

        harness.CategoryService
            .ListAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>(categories));

        foreach (var category in categories)
        {
            harness.CategoryService
                .GetAsync(Arg.Any<Guid>(), category.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<BudgetCategory?>(category));
        }

        return harness;
    }

    [Fact]
    public async Task The_menu_lists_active_and_deactivated_categories()
    {
        var harness = TelegramHarness.Build();
        var active = new BudgetCategory(harness.User.Id, "Mercado", "🛒");
        var inactive = new BudgetCategory(harness.User.Id, "Viajes", "✈️");
        inactive.Deactivate();
        StubCatalogue(harness, active, inactive);

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuCategories), CancellationToken.None);

        turn.NextState.Should().Be("menu");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.CategoryListHeader));
        turn.Responses.Last().Keyboard!.Rows.Should().HaveCount(4, "two categories plus create and budget");
        harness.Conversations.SnapshotOf(harness.User.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task An_empty_catalogue_says_there_are_no_categories()
    {
        var harness = HarnessWith();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuCategories), CancellationToken.None);

        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.CategoryListEmpty));
        turn.Responses.Last().Keyboard!.Rows.Should().HaveCount(2, "create and budget");
    }

    [Fact]
    public async Task Creating_a_category_asks_for_the_name_then_the_icon()
    {
        var harness = HarnessWith();

        var newPrompt = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "menu"), new IncomingCallback("cb", "cats:new"), CancellationToken.None);

        newPrompt.NextState.Should().Be("awaiting-name");
        newPrompt.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.CategoryNamePrompt));
        newPrompt.Responses.Last().Keyboard!.Rows.SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == "cats:list", "the prompt can go back");

        var iconPrompt = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-name"), "Mercado", CancellationToken.None);

        iconPrompt.NextState.Should().Be("awaiting-icon");
        iconPrompt.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.CategoryIconPrompt));
        iconPrompt.Responses.Last().Keyboard!.Rows.SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == "cats:new", "the icon step goes back to the name");
    }

    [Fact]
    public async Task Skipping_the_icon_creates_the_category_with_the_default_icon()
    {
        var harness = HarnessWith();
        var created = new BudgetCategory(harness.User.Id, "Mercado");
        harness.CategoryService
            .CreateAsync(Arg.Any<Guid>(), "Mercado", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CategoryChangeResult.Saved(created)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "awaiting-icon", new CategoriesPayload { Name = "Mercado" }),
            new IncomingCallback("cb", "cats:icon-skip"),
            CancellationToken.None);

        await harness.CategoryService.Received(1).CreateAsync(
            harness.User.Id, "Mercado", null, Arg.Any<CancellationToken>());
        turn.NextState.Should().Be("menu");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.CategoryCreated));
    }

    [Fact]
    public async Task A_blank_name_is_rejected_without_leaving_the_flow()
    {
        var harness = HarnessWith();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-name"), "   ", CancellationToken.None);

        turn.NextState.Should().Be("awaiting-name");
        turn.Responses[0].Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.CategoryNameInvalid, BudgetCategory.MaxNameLength));
        await harness.CategoryService.DidNotReceiveWithAnyArgs().CreateAsync(
            default, default!, default, default);
    }

    [Fact]
    public async Task A_taken_name_returns_to_the_name_prompt()
    {
        var harness = HarnessWith();
        harness.CategoryService
            .CreateAsync(Arg.Any<Guid>(), "Mercado", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CategoryChangeResult.NameTaken()));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "awaiting-icon", new CategoriesPayload { Name = "Mercado" }),
            new IncomingCallback("cb", "cats:icon-skip"),
            CancellationToken.None);

        turn.NextState.Should().Be("awaiting-name");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.CategoryNameTaken));
    }

    [Fact]
    public async Task Reusing_a_deactivated_name_reports_the_reactivation()
    {
        var harness = HarnessWith();
        var reactivated = new BudgetCategory(harness.User.Id, "Mercado");
        harness.CategoryService
            .CreateAsync(Arg.Any<Guid>(), "Mercado", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CategoryChangeResult.Reactivated(reactivated)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "awaiting-icon", new CategoriesPayload { Name = "Mercado" }),
            new IncomingCallback("cb", "cats:icon-skip"),
            CancellationToken.None);

        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.CategoryReactivated));
    }

    [Fact]
    public async Task Opening_a_category_shows_its_actions()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado", "🛒");
        StubCatalogue(harness, category);

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "menu"),
            new IncomingCallback("cb", $"cats:open:{category.Id}"),
            CancellationToken.None);

        turn.NextState.Should().Be("detail");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.CategoryDetailHeader, "🛒", "Mercado"));
        turn.Responses.Last().Keyboard!.Rows.Should().HaveCount(4);
    }

    [Fact]
    public async Task Renaming_a_category_updates_it_and_returns_to_the_detail()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado", "🛒");
        StubCatalogue(harness, category);
        harness.CategoryService
            .RenameAsync(Arg.Any<Guid>(), category.Id, "Supermercado", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CategoryChangeResult.Saved(category)));

        var prompt = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new CategoriesPayload { CategoryId = category.Id }),
            new IncomingCallback("cb", "cats:rename"),
            CancellationToken.None);

        prompt.NextState.Should().Be("awaiting-rename");
        prompt.Responses.Last().Keyboard!.Rows.SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == "cats:detail", "the rename prompt goes back");

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-rename", new CategoriesPayload { CategoryId = category.Id }),
            "Supermercado",
            CancellationToken.None);

        await harness.CategoryService.Received(1).RenameAsync(
            harness.User.Id, category.Id, "Supermercado", Arg.Any<CancellationToken>());
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.CategoryRenamed));
    }

    [Fact]
    public async Task A_name_taken_while_renaming_keeps_the_prompt_open()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado");
        StubCatalogue(harness, category);
        harness.CategoryService
            .RenameAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CategoryChangeResult.NameTaken()));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-rename", new CategoriesPayload { CategoryId = category.Id }),
            "Viajes",
            CancellationToken.None);

        turn.NextState.Should().Be("awaiting-rename");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.CategoryNameTaken));
    }

    [Fact]
    public async Task Toggling_deactivates_an_active_category()
    {
        var harness = TelegramHarness.Build();
        var category = new BudgetCategory(harness.User.Id, "Mercado", "🛒");
        StubCatalogue(harness, category);
        harness.CategoryService
            .SetActiveAsync(Arg.Any<Guid>(), category.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CategoryChangeResult.Saved(category)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new CategoriesPayload { CategoryId = category.Id }),
            new IncomingCallback("cb", "cats:toggle"),
            CancellationToken.None);

        await harness.CategoryService.Received(1).SetActiveAsync(
            harness.User.Id, category.Id, false, Arg.Any<CancellationToken>());
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.CategoryDeactivated));
    }

    [Fact]
    public async Task Cancelling_closes_the_flow()
    {
        var harness = HarnessWith();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "awaiting-name"),
            new IncomingCallback("cb", "cats:cancel"),
            CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.Cancelled));
        harness.Conversations.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_menu_tap_mid_flow_abandons_the_flow_instead_of_being_swallowed()
    {
        var harness = HarnessWith();
        await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuCategories), CancellationToken.None);

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-name"),
            harness.Messages.Get("es", MessageKeys.MenuStatistics),
            CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.FeatureNotReady));
        harness.Conversations.Count.Should().Be(0);
    }

    private static void StubCatalogue(TelegramHarness harness, params BudgetCategory[] categories)
    {
        harness.CategoryService
            .ListAsync(Arg.Any<Guid>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>(categories));

        foreach (var category in categories)
        {
            harness.CategoryService
                .GetAsync(Arg.Any<Guid>(), category.Id, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<BudgetCategory?>(category));
        }
    }
}
