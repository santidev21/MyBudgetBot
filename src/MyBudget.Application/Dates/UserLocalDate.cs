namespace MyBudget.Application.Dates;

/// <summary>
/// Resolves the user's calendar date from the clock, in the user's own time zone.
/// <para>
/// A budget month and an expense date are calendar facts in the user's zone, never UTC. This is
/// the single place that conversion happens, so no flow can quietly get it wrong. The time zone
/// comes from the database, so an unusable value falls back instead of raising.
/// </para>
/// </summary>
public interface IUserLocalDate
{
    DateOnly Today(string timeZoneId);

    DateOnly FromUtc(string timeZoneId, DateTimeOffset instant);
}

/// <inheritdoc />
public sealed class UserLocalDate(TimeProvider timeProvider) : IUserLocalDate
{
    public DateOnly Today(string timeZoneId) => FromUtc(timeZoneId, timeProvider.GetUtcNow());

    public DateOnly FromUtc(string timeZoneId, DateTimeOffset instant)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            // An unusable zone must never break a flow. UTC is the documented fallback, and the
            // onboarding flow validates the zone before it is stored.
            return DateOnly.FromDateTime(instant.UtcDateTime);
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, timeZone).DateTime);
    }
}
