using System.Globalization;
using System.Text;

namespace MyBudget.Application.Dates;

public enum DateParseError
{
    Empty,
    Unrecognized,
    InvalidDate,
    Future,
    TooOld,
}

/// <summary>The outcome of parsing user text into a calendar date.</summary>
public abstract record DateParseResult
{
    private DateParseResult()
    {
    }

    public sealed record Success(DateOnly Date) : DateParseResult;

    public sealed record Invalid(string Input, DateParseError Reason) : DateParseResult;
}

public interface IDateParser
{
    /// <summary>
    /// Parses a date in the user's own calendar terms.
    /// <paramref name="today"/> is the user's local date, so nothing here depends on the
    /// server's time zone.
    /// </summary>
    DateParseResult Parse(string? input, DateOnly today);
}

/// <summary>
/// Parses the date formats a Colombian user actually writes: <c>hoy</c>, <c>ayer</c>,
/// <c>25/09</c>, <c>25-09-2026</c>, <c>2026-09-25</c>, <c>25 de septiembre de 2026</c>.
/// <para>
/// Day-first is assumed and only day-first is accepted for numeric dates. Accepting
/// month-first as a fallback would silently swap day and month for inputs such as
/// <c>03/04</c>, which is exactly the kind of quiet misinterpretation this codebase
/// refuses to make.
/// </para>
/// </summary>
public sealed class DateParser : IDateParser
{
    public const int MinYear = 2000;

    private static readonly IReadOnlyDictionary<string, int> Months = BuildMonths();

    public DateParseResult Parse(string? input, DateOnly today)
    {
        var raw = input ?? string.Empty;
        var text = Normalize(raw);

        if (text.Length == 0)
        {
            return new DateParseResult.Invalid(raw, DateParseError.Empty);
        }

        if (TryNamedDay(text, today, out var named))
        {
            return Validate(raw, named, today);
        }

        if (TryNumeric(text, today, out var numeric))
        {
            return Validate(raw, numeric, today);
        }

        if (TryTextual(text, today, out var textual))
        {
            return Validate(raw, textual, today);
        }

        // The shape was a date but the value was impossible: "31/02", "25/13". Saying so is
        // more useful than "I did not understand".
        return LooksLikeDate(text)
            ? new DateParseResult.Invalid(raw, DateParseError.InvalidDate)
            : new DateParseResult.Invalid(raw, DateParseError.Unrecognized);
    }

    private static bool LooksLikeDate(string text)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 3)
        {
            return false;
        }

        if (tokens.Any(token => Months.ContainsKey(token)))
        {
            return true;
        }

        var parts = text.Split(['/', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is 2 or 3 && parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    private static string Normalize(string input)
    {
        var folded = Text.TextNormalizer.Fold(input);
        var tokens = folded
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token is not ("de" or "del"))
            .ToArray();

        return string.Join(' ', tokens);
    }

    private static bool TryNamedDay(string text, DateOnly today, out DateOnly date)
    {
        switch (text)
        {
            case "hoy":
                date = today;
                return true;
            case "ayer":
                date = today.AddDays(-1);
                return true;
            case "anteayer":
            case "ante ayer":
            case "antier":
                date = today.AddDays(-2);
                return true;
            default:
                date = default;
                return false;
        }
    }

    private static bool TryNumeric(string text, DateOnly today, out DateOnly date)
    {
        date = default;

        var parts = text.Split(['/', '-', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (2 or 3))
        {
            return false;
        }

        if (!IsNumber(parts[0], out var first) || !IsNumber(parts[1], out var second))
        {
            return false;
        }

        // 2026-09-25: year first.
        if (parts.Length == 3 && parts[0].Length == 4)
        {
            return IsNumber(parts[2], out var isoDay)
                   && TryBuild(first, second, isoDay, out date);
        }

        if (parts.Length == 2)
        {
            return TryBuildImplicitYear(second, first, today, out date);
        }

        // A two-digit year is ambiguous, so it is refused rather than guessed.
        return parts[2].Length == 4
               && IsNumber(parts[2], out var year)
               && TryBuild(year, second, first, out date);
    }

    private static bool TryTextual(string text, DateOnly today, out DateOnly date)
    {
        date = default;

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 2 or > 3)
        {
            return false;
        }

        int? month = null;
        int? day = null;
        int? year = null;

        foreach (var token in tokens)
        {
            if (Months.TryGetValue(token, out var monthNumber))
            {
                if (month is not null)
                {
                    return false;
                }

                month = monthNumber;
                continue;
            }

            if (!IsNumber(token, out var value))
            {
                return false;
            }

            if (token.Length == 4)
            {
                if (year is not null)
                {
                    return false;
                }

                year = value;
                continue;
            }

            if (day is not null)
            {
                return false;
            }

            day = value;
        }

        if (month is null || day is null)
        {
            return false;
        }

        return year is { } explicitYear
            ? TryBuild(explicitYear, month.Value, day.Value, out date)
            : TryBuildImplicitYear(month.Value, day.Value, today, out date);
    }

    /// <summary>
    /// A day and month without a year means the most recent occurrence: on 2026-09-25,
    /// <c>25/12</c> is 2025-12-25, because an expense is never in the future.
    /// </summary>
    private static bool TryBuildImplicitYear(int month, int day, DateOnly today, out DateOnly date)
    {
        if (!TryBuild(today.Year, month, day, out date))
        {
            return false;
        }

        if (date > today && TryBuild(today.Year - 1, month, day, out var previousYear))
        {
            date = previousYear;
        }

        return true;
    }

    private static bool TryBuild(int year, int month, int day, out DateOnly date)
    {
        date = default;

        if (year is < 1 or > 9999 || month is < 1 or > 12 || day < 1)
        {
            return false;
        }

        if (day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        date = new DateOnly(year, month, day);
        return true;
    }

    private static DateParseResult Validate(string raw, DateOnly date, DateOnly today)
    {
        if (date.Year < MinYear)
        {
            return new DateParseResult.Invalid(raw, DateParseError.TooOld);
        }

        return date > today
            ? new DateParseResult.Invalid(raw, DateParseError.Future)
            : new DateParseResult.Success(date);
    }

    private static bool IsNumber(string value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private static IReadOnlyDictionary<string, int> BuildMonths()
    {
        var months = new Dictionary<string, int>(StringComparer.Ordinal);

        Add(1, "enero", "ene");
        Add(2, "febrero", "feb");
        Add(3, "marzo", "mar");
        Add(4, "abril", "abr");
        Add(5, "mayo", "may");
        Add(6, "junio", "jun");
        Add(7, "julio", "jul");
        Add(8, "agosto", "ago");
        Add(9, "septiembre", "setiembre", "sep", "set");
        Add(10, "octubre", "oct");
        Add(11, "noviembre", "nov");
        Add(12, "diciembre", "dic");

        return months;

        void Add(int month, params string[] names)
        {
            foreach (var name in names)
            {
                months[name] = month;
            }
        }
    }
}
