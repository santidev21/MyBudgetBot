using FluentAssertions;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The per-user lock is what makes "read state, decide, write state" safe when Telegram
/// delivers two updates from the same chat at once.
/// </summary>
public sealed class UserWorkLockTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Work_for_the_same_user_waits_for_the_lock()
    {
        var userId = Guid.NewGuid();

        await using var holderContext = CreateContext();
        await using var contenderContext = CreateContext();

        var workStarted = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var contenderRan = false;

        var holder = new UserWorkLock(holderContext).ExecuteAsync(
            userId,
            async _ =>
            {
                workStarted.SetResult();
                await release.Task;
                return "holder";
            },
            CancellationToken.None);

        await workStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var contender = new UserWorkLock(contenderContext).ExecuteAsync(
            userId,
            _ =>
            {
                contenderRan = true;
                return Task.FromResult("contender");
            },
            CancellationToken.None);

        // While the first holder is inside the lock, the second must not run.
        await Task.Delay(500);
        contenderRan.Should().BeFalse();

        release.SetResult();

        (await holder).Should().Be("holder");
        (await contender).Should().Be("contender");
        contenderRan.Should().BeTrue();
    }

    [Fact]
    public async Task Work_for_different_users_does_not_wait()
    {
        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();

        var release = new TaskCompletionSource();
        var secondRan = new TaskCompletionSource();

        var first = new UserWorkLock(firstContext).ExecuteAsync(
            Guid.NewGuid(),
            async _ =>
            {
                await release.Task;
                return 1;
            },
            CancellationToken.None);

        // A different user must be able to proceed while the first is still inside.
        var second = new UserWorkLock(secondContext).ExecuteAsync(
            Guid.NewGuid(),
            _ =>
            {
                secondRan.SetResult();
                return Task.FromResult(2);
            },
            CancellationToken.None);

        await secondRan.Task.WaitAsync(TimeSpan.FromSeconds(10));
        (await second).Should().Be(2);

        release.SetResult();
        (await first).Should().Be(1);
    }

    [Fact]
    public async Task A_failing_work_item_releases_the_lock_and_rolls_back()
    {
        var userId = Guid.NewGuid();

        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var lockService = new UserWorkLock(context);

        await FluentActions
            .Awaiting(() => lockService.ExecuteAsync<bool>(
                user.Id,
                _ => throw new InvalidOperationException("work failed"),
                CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        // The lock is gone, so the next attempt proceeds instead of blocking forever.
        var recovered = await new UserWorkLock(CreateContext()).ExecuteAsync(
            user.Id, _ => Task.FromResult("ok"), CancellationToken.None);

        recovered.Should().Be("ok");
    }
}
