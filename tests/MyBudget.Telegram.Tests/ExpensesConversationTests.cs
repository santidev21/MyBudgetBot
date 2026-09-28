using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Expenses;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The expense list, detail and delete flow, driven through the real router.
/// </summary>
public sealed class ExpensesConversationTests
{
    private const long ChatId = 4242;
    private static readonly DateOnly Today = new(2026, 9, 28);

    private static ConversationContext ContextFor(
        TelegramHarness harness, string? state = null, ExpensesPayload? payload = null) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new ConversationSnapshot(
                    harness.User.Id,
                    ChatId,
                    ExpensesConversation.ConversationName,
                    state,
                    (payload ?? new ExpensesPayload()).Serialize(),
                    TestClock.Now.AddMinutes(30)));

    private static ExpenseListItem Item(Guid id, string description = "Verduras") =>
        new(id, Guid.NewGuid(), "Mercado", "🛒", 35_000, description, new DateOnly(2026, 9, 27),
            CategorizationSource.Manual);

    [Fact]
    public async Task The_expenses_menu_lists_the_months_expenses()
    {
        var harness = TelegramHarness.Build();
        var item = Item(Guid.NewGuid());
        harness.ExpenseService
            .ListMonthAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExpenseListItem>>([item]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuExpenses), CancellationToken.None);

        turn.NextState.Should().Be("list");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.ExpenseListHeader, "septiembre 2026"));
        turn.Responses.Last().Keyboard!.Rows
            .SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == $"exps:open:{item.Id}");
    }

    [Fact]
    public async Task An_empty_month_says_there_are_no_expenses()
    {
        var harness = TelegramHarness.Build();
        harness.ExpenseService
            .ListMonthAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExpenseListItem>>([]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuExpenses), CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.ExpenseListEmpty));
    }

    [Fact]
    public async Task Opening_an_expense_shows_its_detail()
    {
        var harness = TelegramHarness.Build();
        var item = Item(Guid.NewGuid());
        harness.ExpenseService
            .GetItemAsync(harness.User.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpenseListItem?>(item));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "list"),
            new IncomingCallback("cb", $"exps:open:{item.Id}"),
            CancellationToken.None);

        turn.NextState.Should().Be("detail");
        var text = turn.Responses.Last().Text;
        text.Should().Contain("$35.000");
        text.Should().Contain("Verduras");
        text.Should().Contain("Mercado");
        turn.Responses.Last().Keyboard!.Rows
            .SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == "exps:delete");
    }

    [Fact]
    public async Task Deleting_asks_for_confirmation_first()
    {
        var harness = TelegramHarness.Build();
        var expenseId = Guid.NewGuid();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new ExpensesPayload().WithExpense(expenseId)),
            new IncomingCallback("cb", "exps:delete"),
            CancellationToken.None);

        turn.NextState.Should().Be("delete-confirm");
        turn.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.ExpenseDeleteConfirm));
        await harness.ExpenseService.DidNotReceiveWithAnyArgs().DeleteAsync(default, default, default);
    }

    [Fact]
    public async Task Confirming_the_delete_removes_it_and_returns_to_the_list()
    {
        var harness = TelegramHarness.Build();
        var expenseId = Guid.NewGuid();
        harness.ExpenseService
            .DeleteAsync(harness.User.Id, expenseId, Arg.Any<CancellationToken>())
            .Returns(true);
        harness.ExpenseService
            .ListMonthAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExpenseListItem>>([]));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "delete-confirm", new ExpensesPayload().WithExpense(expenseId)),
            new IncomingCallback("cb", "exps:delete!"),
            CancellationToken.None);

        await harness.ExpenseService.Received(1).DeleteAsync(
            harness.User.Id, expenseId, Arg.Any<CancellationToken>());
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseDeletedConfirm));
    }

    [Fact]
    public async Task Deleting_an_unknown_expense_says_so()
    {
        var harness = TelegramHarness.Build();
        harness.ExpenseService
            .DeleteAsync(harness.User.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        harness.ExpenseService
            .ListMonthAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExpenseListItem>>([]));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "delete-confirm", new ExpensesPayload().WithExpense(Guid.NewGuid())),
            new IncomingCallback("cb", "exps:delete!"),
            CancellationToken.None);

        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseNotFound));
    }

    [Fact]
    public async Task Opening_an_unknown_expense_reports_not_found()
    {
        var harness = TelegramHarness.Build();
        harness.ExpenseService
            .GetItemAsync(harness.User.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpenseListItem?>(null));
        harness.ExpenseService
            .ListMonthAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ExpenseListItem>>([]));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "list"),
            new IncomingCallback("cb", $"exps:open:{Guid.NewGuid()}"),
            CancellationToken.None);

        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseNotFound));
    }

    [Fact]
    public async Task Cancelling_closes_the_flow()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "list"),
            new IncomingCallback("cb", "exps:cancel"),
            CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.Cancelled));
    }

    [Fact]
    public async Task Editing_offers_the_four_fields()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "detail", new ExpensesPayload().WithExpense(Guid.NewGuid())),
            new IncomingCallback("cb", "exps:edit"),
            CancellationToken.None);

        turn.NextState.Should().Be("edit-menu");
        turn.Responses.Last().Keyboard!.Rows
            .SelectMany(row => row)
            .Select(button => button.CallbackData)
            .Should().Contain(["exps:edit:amount", "exps:edit:description", "exps:edit:category", "exps:edit:date"]);
    }

    [Fact]
    public async Task Editing_the_amount_updates_the_expense()
    {
        var harness = TelegramHarness.Build();
        var item = Item(Guid.NewGuid());
        harness.ExpenseService
            .GetItemAsync(harness.User.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpenseListItem?>(item));
        harness.ExpenseService
            .UpdateAsync(
                Arg.Any<Guid>(), item.Id, Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string?>(),
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ExpenseChangeResult.Ok(
                new Expense(harness.User.Id, item.CategoryId, 40_000, item.Description, item.ExpenseDate, Today))));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "edit-amount", new ExpensesPayload().WithExpense(item.Id)),
            "40.000",
            CancellationToken.None);

        await harness.ExpenseService.Received(1).UpdateAsync(
            harness.User.Id, item.Id, item.CategoryId, 40_000, item.Description, item.ExpenseDate, Today,
            Arg.Any<CancellationToken>());
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseUpdated));
    }

    [Fact]
    public async Task Clearing_the_description_removes_it()
    {
        var harness = TelegramHarness.Build();
        var item = Item(Guid.NewGuid());
        harness.ExpenseService
            .GetItemAsync(harness.User.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpenseListItem?>(item));
        harness.ExpenseService
            .UpdateAsync(
                Arg.Any<Guid>(), item.Id, Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string?>(),
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ExpenseChangeResult.Ok(
                new Expense(harness.User.Id, item.CategoryId, item.Amount, null, item.ExpenseDate, Today))));

        await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "edit-description", new ExpensesPayload().WithExpense(item.Id)),
            new IncomingCallback("cb", "exps:skip"),
            CancellationToken.None);

        await harness.ExpenseService.Received(1).UpdateAsync(
            harness.User.Id, item.Id, item.CategoryId, item.Amount, null, item.ExpenseDate, Today,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editing_the_category_uses_the_chosen_one()
    {
        var harness = TelegramHarness.Build();
        var item = Item(Guid.NewGuid());
        var chosen = new MyBudget.Domain.Categories.BudgetCategory(harness.User.Id, "Restaurantes", "🍽️");
        harness.CategoryService
            .ListAsync(harness.User.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MyBudget.Domain.Categories.BudgetCategory>>([chosen]));
        harness.ExpenseService
            .GetItemAsync(harness.User.Id, item.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpenseListItem?>(item));
        harness.ExpenseService
            .UpdateAsync(
                Arg.Any<Guid>(), item.Id, Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<string?>(),
                Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ExpenseChangeResult.Ok(
                new Expense(harness.User.Id, chosen.Id, item.Amount, item.Description, item.ExpenseDate, Today))));

        await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "edit-category", new ExpensesPayload().WithExpense(item.Id)),
            new IncomingCallback("cb", $"exps:cat:{chosen.Id}"),
            CancellationToken.None);

        await harness.ExpenseService.Received(1).UpdateAsync(
            harness.User.Id, item.Id, chosen.Id, item.Amount, item.Description, item.ExpenseDate, Today,
            Arg.Any<CancellationToken>());
    }
}
