using FluentAssertions;
using MyBudget.Application.Budgets;
using MyBudget.Application.Localization;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Conversations;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The "copy last month's budget" button on a closing. It is a global callback: the closing
/// message outlives any conversation, and the tap must copy the closed month into the new one.
/// </summary>
public sealed class ClosingCopyHandlerTests
{
    private static ConversationContext ContextFor(TelegramHarness harness) =>
        new(harness.User, 4242, null);

    [Fact]
    public async Task Tapping_copy_copies_the_closed_month_into_the_new_one()
    {
        var harness = TelegramHarness.Build();
        harness.BudgetService
            .CopyPreviousMonthAsync(
                harness.User.Id,
                new MonthPeriod(2026, 9),
                new DateOnly(2026, 9, 28),
                Arg.Any<CancellationToken>())
            .Returns(new BudgetCopyResult(BudgetCopyStatus.Copied));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness),
            new IncomingCallback("cb", "v1|closingcopy|2026|8"),
            CancellationToken.None);

        await harness.BudgetService.Received(1).CopyPreviousMonthAsync(
            harness.User.Id,
            new MonthPeriod(2026, 9),
            new DateOnly(2026, 9, 28),
            Arg.Any<CancellationToken>());
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.BudgetCopied));
    }

    [Fact]
    public async Task Tapping_copy_with_no_previous_budget_says_there_is_nothing_to_copy()
    {
        var harness = TelegramHarness.Build();
        harness.BudgetService
            .CopyPreviousMonthAsync(
                harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new BudgetCopyResult(BudgetCopyStatus.NoPreviousBudget));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness),
            new IncomingCallback("cb", "v1|closingcopy|2026|8"),
            CancellationToken.None);

        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.BudgetNoPrevious));
    }

    [Fact]
    public async Task An_unrelated_callback_is_not_claimed_by_the_copy_handler()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness),
            new IncomingCallback("cb", "v1|undo|not-a-guid"),
            CancellationToken.None);

        await harness.BudgetService.DidNotReceiveWithAnyArgs().CopyPreviousMonthAsync(
            default, default, default, Arg.Any<CancellationToken>());
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.ConversationExpired));
    }
}
