using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Categories;
using MyBudget.Application.Localization;
using MyBudget.Application.Recurring;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Recurring;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The recurring-rules flow, driven through the real router.
/// <para>
/// The service is substituted, so what is under test is the conversation: which state each
/// input produces, which service call it triggers, and what the user is told. The calendar
/// arithmetic and persistence are covered by the domain and infrastructure tests.
/// </para>
/// </summary>
public sealed class RecurringConversationTests
{
    private const long ChatId = 4242;
    private static readonly DateOnly Today = new(2026, 9, 28);

    private static ConversationContext ContextFor(
        TelegramHarness harness, string? state = null, RecurringPayload? payload = null) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new ConversationSnapshot(
                    harness.User.Id,
                    ChatId,
                    RecurringConversation.ConversationName,
                    state,
                    (payload ?? new RecurringPayload()).Serialize(),
                    TestClock.Now.AddMinutes(30)));

    private static string ButtonCallback(ConversationTurn turn, string prefix) =>
        turn.Responses.Last().Keyboard!.Rows
            .SelectMany(row => row)
            .Single(button => button.CallbackData!.StartsWith(prefix, StringComparison.Ordinal))
            .CallbackData!;

    private static BudgetCategory Category(TelegramHarness harness, string name = "Arriendo", string icon = "🏠") =>
        new(harness.User.Id, name, icon);

    private static void StubCategories(TelegramHarness harness, params BudgetCategory[] categories)
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

    private static RecurringExpenseView View(
        TelegramHarness harness,
        string categoryName = "Arriendo",
        string icon = "🏠",
        bool active = true,
        string? description = "Arriendo",
        long amount = 900_000,
        int day = 1)
    {
        var categoryId = Guid.NewGuid();
        return new RecurringExpenseView(
            Guid.NewGuid(), categoryId, categoryName, icon, amount, description, day,
            new DateOnly(2026, 1, 1), null, active, null);
    }

    [Fact]
    public async Task The_menu_lists_the_rules_with_a_button_per_rule()
    {
        var harness = TelegramHarness.Build();
        var view = View(harness);
        harness.RecurringService
            .ListAsync(harness.User.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecurringExpenseView>>([view]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness),
            harness.Messages.Get("es", MessageKeys.MenuRecurring),
            CancellationToken.None);

        turn.NextState.Should().Be("list");
        turn.Responses.Last().Text.Should().Contain("Arriendo").And.Contain("900.000");
        turn.Responses.Last().Keyboard!.Rows[0][0].CallbackData.Should().Be($"rec|open|{view.Id}");
    }

    [Fact]
    public async Task No_rules_offers_to_create_one()
    {
        var harness = TelegramHarness.Build();
        harness.RecurringService
            .ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecurringExpenseView>>([]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness),
            harness.Messages.Get("es", MessageKeys.MenuRecurring),
            CancellationToken.None);

        turn.Responses.Last().Text.Should().Be(harness.Messages.Get("es", MessageKeys.RecurringEmpty));
        turn.Responses.Last().Keyboard!.Rows[0][0].CallbackData.Should().Be("rec|new");
    }

    [Fact]
    public async Task The_creation_flow_collects_amount_description_category_and_day()
    {
        var harness = TelegramHarness.Build();
        var category = Category(harness);
        StubCategories(harness, category);
        harness.RecurringService
            .ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecurringExpenseView>>([]));
        harness.RecurringService
            .CreateAsync(
                harness.User.Id, category.Id, 900_000, "Arriendo", 5,
                Arg.Any<DateOnly>(), null, Arg.Any<CancellationToken>())
            .Returns(RecurringChangeResult.Ok(
                new RecurringExpense(
                    harness.User.Id, category.Id, 900_000, "Arriendo", 5, new DateOnly(2026, 9, 28))));

        var start = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuRecurring), CancellationToken.None);
        start.NextState.Should().Be("list");

        var newTurn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "list"), new IncomingCallback("cb", "rec|new"), CancellationToken.None);
        newTurn.NextState.Should().Be("new-amount");

        var amount = await harness.Router.RouteTextAsync(
            ContextFor(harness, "new-amount", new RecurringPayload()), "900.000", CancellationToken.None);
        amount.NextState.Should().Be("new-description");
        amount.NextPayload.Should().Contain("900000");

        var description = await harness.Router.RouteTextAsync(
            ContextFor(harness, "new-description", new RecurringPayload { Amount = 900_000 }),
            "Arriendo",
            CancellationToken.None);
        description.NextState.Should().Be("new-category");

        var chosen = await harness.Router.RouteCallbackAsync(
            ContextFor(
                harness,
                "new-category",
                new RecurringPayload { Amount = 900_000, Description = "Arriendo" }),
            new IncomingCallback("cb", $"rec|cat|{category.Id}"),
            CancellationToken.None);
        chosen.NextState.Should().Be("new-day");

        var day = await harness.Router.RouteTextAsync(
            ContextFor(
                harness,
                "new-day",
                new RecurringPayload
                {
                    Amount = 900_000,
                    Description = "Arriendo",
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    CategoryIcon = category.Icon,
                }),
            "5",
            CancellationToken.None);
        day.NextState.Should().Be("new-confirm");
        day.Responses.Last().Text.Should().Contain("900.000").And.Contain("5");

        var saved = await harness.Router.RouteCallbackAsync(
            ContextFor(
                harness,
                "new-confirm",
                new RecurringPayload
                {
                    Amount = 900_000,
                    Description = "Arriendo",
                    CategoryId = category.Id,
                    CategoryName = category.Name,
                    CategoryIcon = category.Icon,
                    DayOfMonth = 5,
                }),
            new IncomingCallback("cb", "rec|save"),
            CancellationToken.None);

        saved.NextState.Should().Be("list");
        saved.Responses.Select(response => response.Text)
            .Should().Contain(text => text == harness.Messages.Get("es", MessageKeys.RecurringCreated));

        await harness.RecurringService.Received(1).CreateAsync(
            harness.User.Id, category.Id, 900_000, "Arriendo", 5,
            Today, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_invalid_day_is_rejected_and_asked_again()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "new-day", new RecurringPayload { Amount = 1, CategoryId = Guid.NewGuid() }),
            "40",
            CancellationToken.None);

        turn.NextState.Should().Be("new-day");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.RecurringDayInvalid));
    }

    [Fact]
    public async Task The_creation_flow_can_be_cancelled()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "new-amount", new RecurringPayload()),
            new IncomingCallback("cb", "rec|cancel"),
            CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.Cancelled));
        harness.Conversations.Count.Should().Be(0);
    }

    [Fact]
    public async Task Opening_a_rule_shows_its_detail_and_can_pause_it()
    {
        var harness = TelegramHarness.Build();
        var view = View(harness);
        harness.RecurringService
            .GetViewAsync(harness.User.Id, view.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RecurringExpenseView?>(view));
        harness.RecurringService
            .SetActiveAsync(harness.User.Id, view.Id, false, Arg.Any<CancellationToken>())
            .Returns(RecurringChangeResult.Ok(
                new RecurringExpense(
                    harness.User.Id, view.CategoryId, view.Amount, view.Description, view.DayOfMonth,
                    new DateOnly(2026, 1, 1))));

        var detail = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "list"),
            new IncomingCallback("cb", $"rec|open|{view.Id}"),
            CancellationToken.None);

        detail.NextState.Should().Be("detail");
        detail.Responses.Last().Text.Should().Contain("activa");

        var paused = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new RecurringPayload { RuleId = view.Id }),
            new IncomingCallback("cb", $"rec|pause|{view.Id}"),
            CancellationToken.None);

        await harness.RecurringService.Received(1)
            .SetActiveAsync(harness.User.Id, view.Id, false, Arg.Any<CancellationToken>());
        paused.Responses.Select(response => response.Text)
            .Should().Contain(text => text == harness.Messages.Get("es", MessageKeys.RecurringPaused));
    }

    [Fact]
    public async Task Deleting_a_rule_asks_for_confirmation_and_then_removes_it()
    {
        var harness = TelegramHarness.Build();
        var view = View(harness);
        harness.RecurringService
            .GetViewAsync(harness.User.Id, view.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RecurringExpenseView?>(view));
        harness.RecurringService
            .ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecurringExpenseView>>([]));
        harness.RecurringService
            .DeleteAsync(harness.User.Id, view.Id, Arg.Any<CancellationToken>())
            .Returns(true);

        var confirm = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new RecurringPayload { RuleId = view.Id }),
            new IncomingCallback("cb", $"rec|delete|{view.Id}"),
            CancellationToken.None);

        confirm.NextState.Should().Be("delete-confirm");
        ButtonCallback(confirm, "rec|delete-confirm|").Should().Be($"rec|delete-confirm|{view.Id}");

        var deleted = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "delete-confirm", new RecurringPayload { RuleId = view.Id }),
            new IncomingCallback("cb", $"rec|delete-confirm|{view.Id}"),
            CancellationToken.None);

        await harness.RecurringService.Received(1)
            .DeleteAsync(harness.User.Id, view.Id, Arg.Any<CancellationToken>());
        deleted.Responses.Select(response => response.Text)
            .Should().Contain(text => text == harness.Messages.Get("es", MessageKeys.RecurringDeleted));
    }

    [Fact]
    public async Task A_rule_that_no_longer_exists_is_reported_instead_of_crashing()
    {
        var harness = TelegramHarness.Build();
        harness.RecurringService
            .GetViewAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<RecurringExpenseView?>(null));
        harness.RecurringService
            .ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RecurringExpenseView>>([]));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new RecurringPayload { RuleId = Guid.NewGuid() }),
            new IncomingCallback("cb", $"rec|open|{Guid.NewGuid()}"),
            CancellationToken.None);

        turn.Responses.Select(response => response.Text)
            .Should().Contain(text => text == harness.Messages.Get("es", MessageKeys.RecurringNotFound));
    }
}
