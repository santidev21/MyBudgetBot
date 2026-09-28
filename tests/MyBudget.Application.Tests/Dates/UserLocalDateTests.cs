using FluentAssertions;
using MyBudget.Application.Dates;

namespace MyBudget.Application.Tests.Dates;

/// <summary>
/// The conversion from a UTC instant to the user's calendar date.
/// <para>
/// The boundary case is the whole point: an instant late in the UTC day can still be the
/// previous calendar day in Bogotá, and getting that wrong files an expense into the wrong
/// month.
/// </para>
/// </summary>
public sealed class UserLocalDateTests
{
    private static UserLocalDate At(DateTimeOffset now) => new(new StubTimeProvider(now));

    [Fact]
    public void Late_utc_is_still_the_previous_day_in_bogota()
    {
        var clock = At(new DateTimeOffset(2026, 10, 1, 2, 0, 0, TimeSpan.Zero));

        clock.Today("America/Bogota").Should().Be(new DateOnly(2026, 9, 30));
    }

    [Fact]
    public void FromUtc_applies_the_zone_offset()
    {
        var clock = At(DateTimeOffset.UnixEpoch);

        clock.FromUtc("America/Bogota", new DateTimeOffset(2026, 9, 15, 5, 0, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 9, 15));

        clock.FromUtc("America/Bogota", new DateTimeOffset(2026, 9, 15, 4, 59, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 9, 14));
    }

    [Fact]
    public void An_unusable_time_zone_falls_back_to_utc_instead_of_raising()
    {
        var clock = At(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));

        clock.Today("Nowhere/Fake").Should().Be(new DateOnly(2026, 9, 15));
    }

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
