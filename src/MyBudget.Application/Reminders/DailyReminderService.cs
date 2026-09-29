using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Dates;
using MyBudget.Domain.Users;

namespace MyBudget.Application.Reminders;

/// <summary>One user who should receive the daily "log your expenses" reminder right now.</summary>
public sealed record DailyReminder(User User, DateOnly LocalDate);

/// <summary>
/// Decides, exactly once per user and local day, who gets the evening reminder.
/// <para>
/// The trigger is the user's own clock: from <see cref="DailyReminderService.ReminderTime"/> on.
/// A user who already recorded an expense that day is skipped, and the user's setting can turn
/// the reminder off. The claim is written before a reminder is returned, so a scheduler that
/// ticks every minute cannot send twice.
/// </para>
/// </summary>
public interface IDailyReminderService
{
    /// <summary>
    /// The reminders due right now: one per enabled user whose local time is at or past the
    /// reminder hour, who did not log an expense that day and whose day was not claimed yet.
    /// </summary>
    Task<IReadOnlyList<DailyReminder>> PrepareDueAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DailyReminderService(
    IUserRepository users,
    IExpenseReadRepository expenses,
    IReminderDeliveryStore deliveries,
    IUserLocalDate localDate,
    TimeProvider timeProvider) : IDailyReminderService
{
    /// <summary>Marker kind, so other per-day notifications can share the table.</summary>
    public const string Kind = "daily";

    private const int ReminderHour = 21;

    public async Task<IReadOnlyList<DailyReminder>> PrepareDueAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await users.ListAllAsync(cancellationToken);
        var due = new List<DailyReminder>();

        foreach (var user in all)
        {
            if (!user.DailyReminderEnabled)
            {
                continue;
            }

            // The user's own wall clock: before 21:00 (including just after midnight) nothing is
            // due, so a bot that comes back the next morning does not remind about yesterday.
            var local = localDate.LocalNow(user.TimeZone);
            if (local.Hour < ReminderHour)
            {
                continue;
            }

            var today = DateOnly.FromDateTime(local.DateTime);

            // No point reminding somebody who already logged something today.
            if (await expenses.ExistsOnAsync(user.Id, today, cancellationToken))
            {
                continue;
            }

            if (!await deliveries.TryClaimAsync(
                    user.Id, Kind, today, timeProvider.GetUtcNow(), cancellationToken))
            {
                continue;
            }

            due.Add(new DailyReminder(user, today));
        }

        return due;
    }
}
