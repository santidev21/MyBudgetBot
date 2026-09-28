using FluentAssertions;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The inbox is what makes update processing idempotent. Telegram retries webhooks, so a
/// duplicate must be recognised and a failed attempt must be retryable.
/// </summary>
public sealed class UpdateInboxTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task The_first_delivery_is_claimed()
    {
        await using var context = CreateContext();
        var inbox = new UpdateInbox(context, new UnitOfWork(context), TimeProvider.System);

        (await inbox.TryClaimAsync(1001)).Should().BeTrue();
    }

    [Fact]
    public async Task A_completed_update_is_not_claimed_again()
    {
        await using var context = CreateContext();
        var inbox = new UpdateInbox(context, new UnitOfWork(context), TimeProvider.System);

        (await inbox.TryClaimAsync(1002)).Should().BeTrue();
        await inbox.CompleteAsync(1002, null);

        // This is the case Telegram produces on every retry.
        (await inbox.TryClaimAsync(1002)).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_update_can_be_retried()
    {
        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var inbox = new UpdateInbox(context, unitOfWork, TimeProvider.System);

        (await inbox.TryClaimAsync(1003)).Should().BeTrue();
        await inbox.FailAsync(1003, "TimeoutException");

        (await inbox.TryClaimAsync(1003)).Should().BeTrue("a transient failure must be retryable");
        await inbox.CompleteAsync(1003, null);
        (await inbox.TryClaimAsync(1003)).Should().BeFalse();
    }

    [Fact]
    public async Task An_ignored_update_is_not_claimed_again()
    {
        await using var context = CreateContext();
        var inbox = new UpdateInbox(context, new UnitOfWork(context), TimeProvider.System);

        (await inbox.TryClaimAsync(1004)).Should().BeTrue();
        await inbox.IgnoreAsync(1004, null, "stale");

        // Ignored and processed updates are both settled; only a failure reopens a claim.
        (await inbox.TryClaimAsync(1004)).Should().BeFalse();
    }

    [Fact]
    public async Task The_attempt_count_grows_on_each_retry()
    {
        await using var context = CreateContext();
        var unitOfWork = new UnitOfWork(context);
        var inbox = new UpdateInbox(context, unitOfWork, TimeProvider.System);

        await inbox.TryClaimAsync(1005);
        await inbox.FailAsync(1005, "boom");
        await inbox.TryClaimAsync(1005);

        var record = await context.TelegramUpdates.FindAsync(1005L);

        record.Should().NotBeNull();
        record!.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Purge_removes_only_rows_outside_the_retention_window()
    {
        await using (var context = CreateContext())
        {
            var inbox = new UpdateInbox(context, new UnitOfWork(context), TimeProvider.System);

            await inbox.TryClaimAsync(1006);

            (await inbox.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-7)))
                .Should().Be(0, "the row was just created");

            (await inbox.PurgeOlderThanAsync(DateTimeOffset.UtcNow.AddMinutes(1)))
                .Should().Be(1);
        }

        // A new scope, as production uses: the bulk delete bypasses the change tracker, so a
        // reused context would still hold the deleted instance.
        await using (var context = CreateContext())
        {
            var inbox = new UpdateInbox(context, new UnitOfWork(context), TimeProvider.System);

            (await inbox.TryClaimAsync(1006)).Should().BeTrue("the row is gone, so it counts as new");
        }
    }
}
