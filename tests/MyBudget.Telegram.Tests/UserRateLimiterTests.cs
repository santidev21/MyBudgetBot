using FluentAssertions;
using Microsoft.Extensions.Options;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.RateLimiting;
using MyBudget.Telegram.Tests.Fakes;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The per-user inbound throttle. It protects the bot from a message flood without silently
/// punishing a normal conversation, so the window and the notice behaviour are both pinned.
/// </summary>
public sealed class UserRateLimiterTests
{
    private static SlidingWindowUserRateLimiter Build(FixedClock clock, int perMinute) =>
        new(Microsoft.Extensions.Options.Options.Create(
            new TelegramOptions { UserRateLimitPerMinute = perMinute }), clock);

    [Fact]
    public void Updates_up_to_the_budget_are_allowed()
    {
        var clock = FixedClock.At(TestClock.Now);
        var limiter = Build(clock, perMinute: 3);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
    }

    [Fact]
    public void The_first_update_over_the_budget_carries_a_notice_and_the_rest_do_not()
    {
        var clock = FixedClock.At(TestClock.Now);
        var limiter = Build(clock, perMinute: 1);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.ThrottledAndNotified);

        // Answering every dropped update would turn the throttle into the flood it prevents.
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Throttled);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Throttled);
    }

    [Fact]
    public void The_window_slides_so_a_quiet_minute_restores_the_budget()
    {
        var clock = FixedClock.At(TestClock.Now);
        var limiter = Build(clock, perMinute: 2);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.ThrottledAndNotified);

        clock.Now = clock.Now.AddSeconds(61);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
    }

    [Fact]
    public void A_sliding_window_expires_entries_individually_rather_than_all_at_once()
    {
        // With a fixed window a user could spend a full budget either side of the boundary.
        // Here the first update of the pair ages out 30 seconds before the second.
        var clock = FixedClock.At(TestClock.Now);
        var limiter = Build(clock, perMinute: 2);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);

        clock.Now = clock.Now.AddSeconds(30);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.ThrottledAndNotified);

        clock.Now = clock.Now.AddSeconds(31); // 61s after the first, 31s after the second
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
    }

    [Fact]
    public void A_second_notice_is_sent_once_the_user_is_still_flooding_a_minute_later()
    {
        var clock = FixedClock.At(TestClock.Now);
        var limiter = Build(clock, perMinute: 1);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.ThrottledAndNotified);

        // A minute later the single allowed slot is free again; the user spends it and floods.
        clock.Now = clock.Now.AddSeconds(60);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(111).Should().Be(RateLimitDecision.ThrottledAndNotified);
    }

    [Fact]
    public void Each_user_has_an_independent_budget()
    {
        var clock = FixedClock.At(TestClock.Now);
        var limiter = Build(clock, perMinute: 1);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.Allowed);
        limiter.Evaluate(222).Should().Be(RateLimitDecision.Allowed);

        limiter.Evaluate(111).Should().Be(RateLimitDecision.ThrottledAndNotified);
        limiter.Evaluate(222).Should().Be(RateLimitDecision.ThrottledAndNotified);
    }
}
