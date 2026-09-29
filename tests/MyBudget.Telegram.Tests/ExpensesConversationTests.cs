using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Expenses;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Presentation;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The expense history, detail and delete flow, driven through the real router.
/// </summary>
public sealed class ExpensesConversationTests
{
    private const long ChatId = 4242;
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly DateRange SeptemberRange = DateRange.ForMonth(new MonthPeriod(2026, 9));

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

    private static ExpenseListItem Item(
        Guid id, string description = "Verduras", DateOnly? date = null) =>
        new(id, Guid.NewGuid(), "Mercado", "🛒", 35_000, description, date ?? new DateOnly(2026, 9, 27),
            CategorizationSource.Manual);

    private static void StubHistory(
        TelegramHarness harness, IReadOnlyList<ExpenseListItem> items, ExpensePageCursor? next = null) =>
        harness.ReportService
            .GetHistoryAsync(
                harness.User.Id,
                Arg.Any<DateRange>(),
                Arg.Any<ExpensePageCursor?>(),
                Arg.Any<int>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExpenseHistoryPage(SeptemberRange, items, next)));

    private static IReadOnlyList<BotButton> Buttons(ConversationTurn turn) =>
        turn.Responses.Last().Keyboard!.Rows.SelectMany(row => row).ToList();

    [Fact]
    public async Task The_expenses_menu_lists_the_current_month_grouped_by_day()
    {
        var harness = TelegramHarness.Build();
        var newest = Item(Guid.NewGuid(), "Verduras");
        var older = Item(Guid.NewGuid(), "Taxi", new DateOnly(2026, 9, 26));
        StubHistory(harness, [newest, older]);

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuExpenses), CancellationToken.None);

        turn.NextState.Should().Be("list");
        turn.Responses.Last().Text.Should().Be(
            """
            Gastos del 01/09/2026 – 30/09/2026

            27 de septiembre de 2026
            • Verduras — $35.000
            Total del día: $35.000

            26 de septiembre de 2026
            • Taxi — $35.000
            Total del día: $35.000
            """);

        Buttons(turn).Should().Contain(button => button.CallbackData == $"exps:open:{newest.Id}");
        Buttons(turn).Should().Contain(button => button.CallbackData == "exps:range:month");
    }

    [Fact]
    public async Task An_empty_range_says_there_are_no_expenses_and_keeps_the_range_buttons()
    {
        var harness = TelegramHarness.Build();
        StubHistory(harness, []);

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuExpenses), CancellationToken.None);

        turn.Completed.Should().BeFalse();
        turn.NextState.Should().Be("list");
        turn.Responses.Last().Text.Should().Be(
            """
            Gastos del 01/09/2026 – 30/09/2026

            No hay gastos en este rango.
            """);

        Buttons(turn).Select(button => button.CallbackData).Should()
            .Contain(["exps:range:month", "exps:range:prev", "exps:range:3m", "exps:range:year"]);
    }

    [Fact]
    public async Task See_more_resumes_from_the_next_page_cursor()
    {
        var harness = TelegramHarness.Build();
        var cursor = new ExpensePageCursor(new DateOnly(2026, 9, 20), Guid.NewGuid());
        var secondPage = Item(Guid.NewGuid(), "Antiguo", new DateOnly(2026, 9, 10));

        harness.ReportService
            .GetHistoryAsync(
                harness.User.Id, Arg.Any<DateRange>(), cursor, Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ExpenseHistoryPage(SeptemberRange, [secondPage], null)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "list", new ExpensesPayload().WithPage(null, cursor, [])),
            new IncomingCallback("cb", "exps:more"),
            CancellationToken.None);

        await harness.ReportService.Received(1).GetHistoryAsync(
            harness.User.Id, Arg.Any<DateRange>(), cursor, Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        ExpensesPayload.Parse(turn.NextPayload).Next.Should().BeNull();
        Buttons(turn).Should().Contain(button => button.CallbackData == "exps:prevpage");
    }

    [Fact]
    public async Task Previous_page_steps_back_to_the_first_page()
    {
        var harness = TelegramHarness.Build();
        var cursor = new ExpensePageCursor(new DateOnly(2026, 9, 20), Guid.NewGuid());
        StubHistory(harness, []);

        await harness.Router.RouteCallbackAsync(
            ContextFor(
                harness, "list",
                new ExpensesPayload
                {
                    AfterDate = cursor.ExpenseDate,
                    AfterId = cursor.ExpenseId,
                }),
            new IncomingCallback("cb", "exps:prevpage"),
            CancellationToken.None);

        await harness.ReportService.Received(1).GetHistoryAsync(
            harness.User.Id, Arg.Any<DateRange>(), null, Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_range_preset_switches_the_reporting_range()
    {
        var harness = TelegramHarness.Build();
        StubHistory(harness, []);

        await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "list"),
            new IncomingCallback("cb", "exps:range:prev"),
            CancellationToken.None);

        await harness.ReportService.Received(1).GetHistoryAsync(
            harness.User.Id,
            new DateRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            null,
            Arg.Any<int>(),
            Arg.Any<Guid?>(),
            Arg.Any<CancellationToken>());
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
        StubHistory(harness, []);

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
        StubHistory(harness, []);

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
        StubHistory(harness, []);

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
