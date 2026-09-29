using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using MyBudget.Telegram.Options;

namespace MyBudget.Telegram.RateLimiting;

/// <summary>What the per-user throttle decided about one inbound update.</summary>
internal enum RateLimitDecision
{
    /// <summary>Within budget; the update may be processed.</summary>
    Allowed,

    /// <summary>Over budget. The update is dropped and no reply is sent, to avoid amplifying the flood.</summary>
    Throttled,

    /// <summary>Over budget, and the user has not been told during this window yet.</summary>
    ThrottledAndNotified,
}

/// <summary>
/// Decides whether one update from a Telegram user may be processed. Keyed by Telegram user id
/// because the throttle runs before a user record is resolved: a flood must be cheap to refuse.
/// </summary>
internal interface IUserRateLimiter
{
    RateLimitDecision Evaluate(long telegramUserId);
}

/// <summary>
/// A per-user sliding-window throttle held in memory.
/// <para>
/// State is per process, which is correct here: the deployment is a single instance, and losing
/// the counters on restart only forgives a burst. A sliding window is used rather than a fixed
/// one because a fixed window lets a user spend the whole budget at 12:59:59 and again at
/// 13:00:00, doubling the effective rate exactly when it matters.
/// </para>
/// </summary>
internal sealed class SlidingWindowUserRateLimiter(
    IOptions<TelegramOptions> options,
    TimeProvider timeProvider) : IUserRateLimiter
{
    private readonly ConcurrentDictionary<long, Window> _windows = new();

    public RateLimitDecision Evaluate(long telegramUserId)
    {
        var limit = options.Value.UserRateLimitPerMinute;
        var window = options.Value.UserRateLimitWindow;
        var now = timeProvider.GetUtcNow();

        var entry = _windows.GetOrAdd(telegramUserId, _ => new Window());

        lock (entry)
        {
            while (entry.Timestamps.Count > 0 && now - entry.Timestamps.Peek() >= window)
            {
                entry.Timestamps.Dequeue();
            }

            if (entry.Timestamps.Count < limit)
            {
                entry.Timestamps.Enqueue(now);
                return RateLimitDecision.Allowed;
            }

            // One notice per window. Answering every dropped update would turn the throttle
            // into the flood it exists to prevent, and each reply also costs an outbound slot.
            if (entry.LastNotice is not { } lastNotice || now - lastNotice >= window)
            {
                entry.LastNotice = now;
                return RateLimitDecision.ThrottledAndNotified;
            }

            return RateLimitDecision.Throttled;
        }
    }

    private sealed class Window
    {
        public Queue<DateTimeOffset> Timestamps { get; } = new();

        public DateTimeOffset? LastNotice { get; set; }
    }
}
