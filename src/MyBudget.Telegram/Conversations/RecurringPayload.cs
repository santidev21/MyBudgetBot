using System.Text.Json;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// State for the recurring-rules flow: which rule a detail or delete screen refers to, and the
/// draft of a rule being created.
/// <para>
/// JSON because the column is <c>jsonb</c>, which PostgreSQL canonicalises; it is only ever
/// read back through this type. A payload that cannot be read is treated as empty.
/// </para>
/// </summary>
internal sealed record RecurringPayload
{
    private static readonly RecurringPayload Empty = new();

    /// <summary>The rule the detail, pause or delete screen refers to.</summary>
    public Guid? RuleId { get; init; }

    public long? Amount { get; init; }

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    public string? CategoryIcon { get; init; }

    public int? DayOfMonth { get; init; }

    public static RecurringPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<RecurringPayload>(payload) ?? Empty;
        }
        catch (JsonException)
        {
            // A payload written by a different version of the flow. Starting over is safer than
            // acting on a half-understood draft.
            return Empty;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    public RecurringPayload WithRule(Guid ruleId) => this with { RuleId = ruleId };
}
