using System.Text.Json;
using System.Text.Json.Serialization;
using MyBudget.Domain.Budgets;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// State for the month-reporting screens: which month the summary or the statistics show.
/// JSON because the column is <c>jsonb</c>, which PostgreSQL canonicalises; it is only ever
/// read back through this type.
/// </summary>
internal sealed record ReportingPayload
{
    private static readonly ReportingPayload Empty = new();

    public int? Year { get; init; }

    public int? Month { get; init; }

    /// <summary>
    /// The month carried by the payload, or <c>null</c> when it is absent or not a real month.
    /// A payload written by an older version must not raise, so it is validated here rather
    /// than trusted.
    /// </summary>
    [JsonIgnore]
    public MonthPeriod? Period
    {
        get
        {
            if (Year is not { } year || Month is not { } month
                || year is < MonthPeriod.MinYear or > MonthPeriod.MaxYear
                || month is < 1 or > 12)
            {
                return null;
            }

            return new MonthPeriod(year, month);
        }
    }

    public static ReportingPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ReportingPayload>(payload) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    public ReportingPayload WithPeriod(MonthPeriod period) =>
        this with { Year = period.Year, Month = period.Month };
}
