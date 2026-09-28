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
/// The matching half of the expense flow, driven through the real router and the real matcher.
/// <para>
/// A recognized description suggests, a tie asks, and an unrecognized one offers to learn the
/// term as a keyword — showing exactly what will be stored and warning when it already belongs to
/// another category.
/// </para>
/// </summary>
public sealed class ExpenseMatchingConversationTests
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

    private static BudgetCategory Category(TelegramHarness harness, string name, params string[] aliases)
    {
        var category = new BudgetCategory(harness.User.Id, name, "🔖");
        foreach (var alias in aliases)
        {
            category.AddAlias(alias, alias);
        }

        return category;
    }

    private static void StubCategories(TelegramHarness harness, params BudgetCategory[] categories) =>
        harness.CategoryService
            .ListAsync(harness.User.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>(categories));

    [Fact]
    public async Task A_recognized_description_suggests_the_category_in_the_confirmation()
    {
        var harness = TelegramHarness.Build();
        var mercado = Category(harness, "Mercado", "verduras");
        StubCategories(harness, mercado);

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), "35.000 verduras", CancellationToken.None);

        turn.NextState.Should().Be("confirm");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseCategorySuggestion, "🔖 Mercado"));
        turn.Responses.Last().Text.Should().Contain("Mercado");
        await harness.PendingActions.Received(1).CreateAsync(
            Arg.Is<PendingAction>(action => action.Action == "expense"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_accepted_suggestion_records_the_matched_source()
    {
        var harness = TelegramHarness.Build();
        var mercado = Category(harness, "Mercado", "verduras");
        StubCategories(harness, mercado);

        PendingAction? pending = null;
        harness.PendingActions
            .CreateAsync(Arg.Do<PendingAction>(action => pending = action), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await harness.Router.RouteTextAsync(
            ContextFor(harness), "35.000 verduras", CancellationToken.None);

        pending.Should().NotBeNull();
        harness.PendingActions
            .ConsumeAsync(harness.User.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PendingAction?>(pending));

        var expense = new Expense(harness.User.Id, mercado.Id, 35_000, "verduras", Today, Today);
        harness.ExpenseService
            .CreateAsync(
                Arg.Any<Guid>(), mercado.Id, 35_000, "verduras", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
                Arg.Any<CategorizationSource>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ExpenseChangeResult.Ok(expense)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "confirm"),
            new IncomingCallback("cb", $"v1|expense|{Guid.NewGuid()}"),
            CancellationToken.None);

        turn.Completed.Should().BeTrue();
        await harness.ExpenseService.Received(1).CreateAsync(
            harness.User.Id, mercado.Id, 35_000, "verduras", Today, Today,
            CategorizationSource.Matched, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_ambiguous_description_asks_which_category()
    {
        var harness = TelegramHarness.Build();
        StubCategories(
            harness,
            Category(harness, "Restaurantes", "comida"),
            Category(harness, "Mercado", "comida"));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), "35.000 comida", CancellationToken.None);

        turn.NextState.Should().Be("category");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseCategoryAmbiguous));
    }

    [Fact]
    public async Task A_typo_does_not_match_while_fuzzy_is_off_by_default()
    {
        var harness = TelegramHarness.Build();
        StubCategories(harness, Category(harness, "Mercado", "mercado"));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), "35.000 mercad", CancellationToken.None);

        turn.NextState.Should().Be("category");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseCategoryNone));
    }

    [Fact]
    public async Task An_unrecognized_term_offers_to_be_saved_after_choosing_a_category()
    {
        var harness = TelegramHarness.Build();
        var mercado = Category(harness, "Mercado");
        StubCategories(harness, mercado);
        harness.CategoryService
            .GetAsync(harness.User.Id, mercado.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BudgetCategory?>(mercado));

        var picker = await harness.Router.RouteTextAsync(
            ContextFor(harness), "35.000 verduras", CancellationToken.None);
        picker.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseCategoryNone));

        var payload = new ExpensePayload
        {
            Amount = 35_000,
            Description = "verduras",
            LearnTerm = "verduras",
            Source = CategorizationSource.Manual,
        };

        var learn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "category", payload),
            new IncomingCallback("cb", $"exp:cat:{mercado.Id}"),
            CancellationToken.None);

        learn.NextState.Should().Be("learn-keyword");
        learn.Responses.Last().Text.Should().Contain("verduras").And.Contain("Mercado");
    }

    [Fact]
    public async Task Saving_the_learned_term_stores_it_and_continues_to_the_confirmation()
    {
        var harness = TelegramHarness.Build();
        var mercado = Category(harness, "Mercado");
        harness.CategoryService
            .AddAliasAsync(harness.User.Id, mercado.Id, "verduras", false, Arg.Any<CancellationToken>())
            .Returns(AliasChangeResult.Added());

        var payload = new ExpensePayload
        {
            Amount = 35_000,
            Description = "verduras",
            CategoryId = mercado.Id,
            CategoryName = "Mercado",
            CategoryIcon = "🔖",
            LearnTerm = "verduras",
            Source = CategorizationSource.Manual,
        };

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "learn-keyword", payload),
            new IncomingCallback("cb", "exp:learn:save"),
            CancellationToken.None);

        turn.NextState.Should().Be("confirm");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.ExpenseLearnKeywordSaved, "verduras", "🔖 Mercado"));
        await harness.CategoryService.Received(1).AddAliasAsync(
            harness.User.Id, mercado.Id, "verduras", false, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_learned_term_owned_elsewhere_warns_before_storing_anyway()
    {
        var harness = TelegramHarness.Build();
        var mercado = Category(harness, "Mercado");
        var other = new BudgetCategory(harness.User.Id, "Restaurantes");
        harness.CategoryService
            .AddAliasAsync(harness.User.Id, mercado.Id, "comida", false, Arg.Any<CancellationToken>())
            .Returns(AliasChangeResult.Conflict([other]));

        var payload = new ExpensePayload
        {
            Amount = 35_000,
            Description = "comida",
            CategoryId = mercado.Id,
            CategoryName = "Mercado",
            CategoryIcon = "🔖",
            LearnTerm = "comida",
            Source = CategorizationSource.Manual,
        };

        var conflict = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "learn-keyword", payload),
            new IncomingCallback("cb", "exp:learn:save"),
            CancellationToken.None);

        conflict.NextState.Should().Be("learn-keyword");
        conflict.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.AliasConflict, "Restaurantes"));

        harness.CategoryService
            .AddAliasAsync(harness.User.Id, mercado.Id, "comida", true, Arg.Any<CancellationToken>())
            .Returns(AliasChangeResult.Added());

        var confirmed = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "learn-keyword", payload),
            new IncomingCallback("cb", "exp:learn:confirm"),
            CancellationToken.None);

        confirmed.NextState.Should().Be("confirm");
        await harness.CategoryService.Received(1).AddAliasAsync(
            harness.User.Id, mercado.Id, "comida", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skipping_the_learn_offer_goes_straight_to_the_confirmation()
    {
        var harness = TelegramHarness.Build();
        var mercado = Category(harness, "Mercado");

        var payload = new ExpensePayload
        {
            Amount = 35_000,
            Description = "verduras",
            CategoryId = mercado.Id,
            CategoryName = "Mercado",
            CategoryIcon = "🔖",
            LearnTerm = "verduras",
            Source = CategorizationSource.Manual,
        };

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "learn-keyword", payload),
            new IncomingCallback("cb", "exp:learn:skip"),
            CancellationToken.None);

        turn.NextState.Should().Be("confirm");
        await harness.CategoryService.DidNotReceiveWithAnyArgs()
            .AddAliasAsync(default, default, default!, default, default);
    }
}
