using System.Globalization;
using MyBudget.Domain.Budgets;

namespace MyBudget.Telegram.Reporting;

/// <summary>
/// The callback data on the "copy last month's budget" button of a closing.
/// <para>
/// It carries the closed month only, never an amount: the handler derives the new month from it
/// and the copy always reads the previous month through the ownership-scoped service. The whole
/// payload is well inside Telegram's 64-byte cap.
/// </para>
/// </summary>
internal static class ClosingCopyCallback
{
    private const string Prefix = "v1|closingcopy|";

    public static string ForPeriod(MonthPeriod period) =>
        $"{Prefix}{period.Year.ToString(CultureInfo.InvariantCulture)}|" +
        period.Month.ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string data, out MonthPeriod period)
    {
        period = default;

        if (!data.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = data[Prefix.Length..].Split('|');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month)
            || year is < MonthPeriod.MinYear or > MonthPeriod.MaxYear
            || month is < 1 or > 12)
        {
            return false;
        }

        period = new MonthPeriod(year, month);
        return true;
    }
}
