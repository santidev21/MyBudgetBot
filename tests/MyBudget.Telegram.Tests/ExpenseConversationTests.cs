using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Categories;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The guided expense flow, driven through the real router.
/// <para>
/// The confirmation is the part worth pinning: the draft is stored server side, claimed once
/// through the pending action store, and the callback carries only its identifier.
/// </para>
/// </summary>
public sealed class ExpenseConversationTests
{
    private const long ChatId = 4242;
    private static readonly DateOnly Today = new(2026, 9, 28);

    private static ConversationContext ContextFor(
        TelegramHarness harness, string? state = null, ExpensePayload? payload = null) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new ConversationSnapshot(
                    harness.User.Id,
                    ChatId,
                    ExpenseConversation.ConversationName,
                    state,
                    (payload ?? new ExpensePayload()).Serialize(),
                    TestClock.Now.AddMinutes(30)));

    private static string ButtonCallback(ConversationTurn turn, string prefix) =>
        turn.Responses.Last().Keyboard!.Rows
            .SelectMany(row => row)
            .Single(button => button.CallbackData!.StartsWith(prefix, StringComparison.Ordinal))
            .CallbackData!;

    private static BudgetCategory Category(TelegramHarness harness, string name = "Mercado") =>
        new(harness.User.Id, name, "🛒");

    [Fact]
    public async Task The_add_expense_menu_starts_by_asking_for_the_amount()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuAddExpense), CancellationToken.None);

        turn.NextState.Should().Be("awaiting-amount");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.AmountPrompt));
    }

    [Fact]
    public async Task A_valid_amount_asks_for_a_description()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-amount"), "35.000", CancellationToken.None);

        turn.NextState.Should().Be("awaiting-description");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.ExpenseDescriptionPrompt));
    }

    [Fact]
    public async Task An_unrecognized_amount_is_rejected_and_asked_again()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-amount"), "abc", CancellationToken.None);

        turn.NextState.Should().Be("awaiting-amount");
        turn.Responses[0].Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.AmountInvalid));
    }

    [Fact]
    public async Task Skipping_the_description_shows_the_category_picker()
    {
        var harness = TelegramHarness.Build();
        var category = Category(harness);
        harness.CategoryService
            .ListAsync(harness.User.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>([category]));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "awaiting-description", new ExpensePayload { Amount = 35_000 }),
            new IncomingCallback("cb", "exp:skip"),
            CancellationToken.None);

        turn.NextState.Should().Be("category");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.ExpenseCategoryPrompt));
        turn.Responses.Last().Keyboard!.Rows
            .SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == $"exp:cat:{category.Id}");
    }

    [Fact]
    public async Task Choosing_a_category_shows_the_confirmation_and_stores_a_pending_action()
    {
        var harness = TelegramHarness.Build();
        var category = Category(harness);
        harness.CategoryService
            .GetAsync(harness.User.Id, category.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BudgetCategory?>(category));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(
                harness, "category",
                new ExpensePayload { Amount = 35_000, Description = "Verduras" }),
            new IncomingCallback("cb", $"exp:cat:{category.Id}"),
            CancellationToken.None);

        turn.NextState.Should().Be("confirm");
        var text = turn.Responses.Last().Text;
        text.Should().Contain("$35.000");
        text.Should().Contain("Verduras");
        text.Should().Contain("Mercado");
        await harness.PendingActions.Received(1).CreateAsync(
            Arg.Is<PendingAction>(action => action.Action == "expense"),
            Arg.Any<CancellationToken>());
        ButtonCallback(turn, "v1|expense|").Should().StartWith("v1|expense|");
    }

    [Fact]
    public async Task Confirming_registers_the_expense_once_and_offers_undo()
    {
        var harness = TelegramHarness.Build();
        var categoryId = Guid.NewGuid();
        var draft = new ExpensePayload
        {
            Amount = 35_000,
            Description = "Verduras",
            CategoryId = categoryId,
            CategoryName = "Mercado",
            CategoryIcon = "🛒",
            Date = Today,
        };
        var expense = new Expense(harness.User.Id, categoryId, 35_000, "Verduras", Today, Today);
        harness.PendingActions
            .ConsumeAsync(harness.User.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PendingAction?>(
                new PendingAction(Guid.NewGuid(), harness.User.Id, "expense", draft.Serialize(), TestClock.Now.AddMinutes(30))));
        harness.ExpenseService
            .CreateAsync(
                Arg.Any<Guid>(), categoryId, 35_000, "Verduras", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CategorizationSource>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ExpenseChangeResult.Ok(expense)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "confirm", draft),
            new IncomingCallback("cb", $"v1|expense|{Guid.NewGuid()}"),
            CancellationToken.None);

        await harness.ExpenseService.Received(1).CreateAsync(
            harness.User.Id, categoryId, 35_000, "Verduras", Today, Today,
            CategorizationSource.Manual, Arg.Any<CancellationToken>());
        turn.Completed.Should().BeTrue();
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseRegistered));
        turn.Responses.Last().Keyboard!.Rows
            .SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == $"v1|undo|{expense.Id}");
    }

    [Fact]
    public async Task An_expired_confirmation_is_reported_instead_of_registering()
    {
        var harness = TelegramHarness.Build();
        harness.PendingActions
            .ConsumeAsync(harness.User.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PendingAction?>(null));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "confirm", new ExpensePayload { Amount = 35_000, CategoryId = Guid.NewGuid() }),
            new IncomingCallback("cb", $"v1|expense|{Guid.NewGuid()}"),
            CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseExpired));
        await harness.ExpenseService.DidNotReceiveWithAnyArgs().CreateAsync(
            default, default, default, default, default, default, default, default);
    }

    [Fact]
    public async Task Choosing_a_date_rebuilds_the_confirmation_with_it()
    {
        var harness = TelegramHarness.Build();
        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(
                harness, "confirm",
                new ExpensePayload
                {
                    Amount = 35_000,
                    CategoryId = Guid.NewGuid(),
                    CategoryName = "Mercado",
                    CategoryIcon = "🛒",
                    Date = Today,
                }),
            new IncomingCallback("cb", "exp:yesterday"),
            CancellationToken.None);

        turn.NextState.Should().Be("confirm");
        turn.Responses.Last().Text.Should().Contain("27 de septiembre de 2026");
    }

    [Fact]
    public async Task A_future_date_is_refused()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(
                harness, "awaiting-date",
                new ExpensePayload { Amount = 35_000, CategoryId = Guid.NewGuid() }),
            "30/09/2026",
            CancellationToken.None);

        turn.NextState.Should().Be("awaiting-date");
        turn.Responses[0].Text.Should().Be(harness.Messages.Get("es", MessageKeys.DateFuture));
    }

    [Fact]
    public async Task Cancelling_closes_the_flow()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "awaiting-amount", new ExpensePayload()),
            new IncomingCallback("cb", "exp:cancel"),
            CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.Cancelled));
    }

    [Fact]
    public async Task Undo_deletes_the_expense_without_an_active_conversation()
    {
        var harness = TelegramHarness.Build();
        var expenseId = Guid.NewGuid();
        harness.ExpenseService
            .DeleteAsync(harness.User.Id, expenseId, Arg.Any<CancellationToken>())
            .Returns(true);

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness),
            new IncomingCallback("cb", $"v1|undo|{expenseId}"),
            CancellationToken.None);

        await harness.ExpenseService.Received(1).DeleteAsync(
            harness.User.Id, expenseId, Arg.Any<CancellationToken>());
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.ExpenseUndone));
    }

    [Fact]
    public async Task Undo_for_an_unknown_expense_says_so()
    {
        var harness = TelegramHarness.Build();
        harness.ExpenseService
            .DeleteAsync(harness.User.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness),
            new IncomingCallback("cb", $"v1|undo|{Guid.NewGuid()}"),
            CancellationToken.None);

        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.ExpenseNotFound));
    }

    [Fact]
    public async Task A_compact_message_keeps_its_amount_and_description_and_asks_for_the_category()
    {
        var harness = TelegramHarness.Build();
        var category = Category(harness);
        harness.CategoryService
            .ListAsync(harness.User.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>([category]));
        harness.CategoryService
            .GetAsync(harness.User.Id, category.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BudgetCategory?>(category));

        var picker = await harness.Router.RouteTextAsync(
            ContextFor(harness), "35.000 verduras", CancellationToken.None);

        picker.NextState.Should().Be("category");
        picker.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.ExpenseCategoryPrompt));

        var confirmation = await harness.Router.RouteCallbackAsync(
            ContextFor(
                harness, "category",
                new ExpensePayload { Amount = 35_000, Description = "verduras" }),
            new IncomingCallback("cb", $"exp:cat:{category.Id}"),
            CancellationToken.None);

        confirmation.Responses.Last().Text.Should().Contain("$35.000");
        confirmation.Responses.Last().Text.Should().Contain("verduras");
    }

    [Fact]
    public async Task A_bare_amount_asks_for_the_description()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), "35000", CancellationToken.None);

        turn.NextState.Should().Be("awaiting-description");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.ExpenseDescriptionPrompt));
    }

    [Fact]
    public async Task An_ambiguous_amount_asks_which_one_instead_of_guessing()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), "12 y 15", CancellationToken.None);

        turn.NextState.Should().Be("awaiting-amount");
        turn.Responses[0].Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.ExpenseAmbiguousAmount));
    }

    [Fact]
    public async Task Text_without_an_amount_is_not_an_entry_and_falls_back_to_help()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), "hola qué tal", CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.Help));
    }
}
