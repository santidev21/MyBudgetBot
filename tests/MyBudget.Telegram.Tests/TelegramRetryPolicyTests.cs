using FluentAssertions;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Tests;

public sealed class TelegramRetryPolicyTests
{
    [Fact]
    public void A_missing_retry_after_falls_back_to_a_short_pause()
    {
        TelegramRetryPolicy.DelayFor(null).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void A_non_positive_retry_after_also_falls_back(int retryAfter) =>
        TelegramRetryPolicy.DelayFor(retryAfter).Should().Be(TimeSpan.FromSeconds(1));

    [Fact]
    public void A_normal_retry_after_is_honoured() =>
        TelegramRetryPolicy.DelayFor(7).Should().Be(TimeSpan.FromSeconds(7));

    [Fact]
    public void An_absurd_retry_after_is_capped_so_the_pipeline_is_not_parked()
    {
        // Telegram would reject the update and redeliver the webhook; waiting hours inline
        // would hold the request open far longer than that.
        TelegramRetryPolicy.DelayFor(600_000).Should().Be(TimeSpan.FromSeconds(60));
    }
}
