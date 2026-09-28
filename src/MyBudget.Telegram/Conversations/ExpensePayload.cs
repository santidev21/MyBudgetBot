using System.Text.Json;
using MyBudget.Domain.Expenses;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The draft of an expense being entered, carried between steps as the conversation payload.
/// <para>
/// JSON because the column is <c>jsonb</c>, which PostgreSQL canonicalises, so it is only ever
/// read back through this type and never compared as a string. Nothing here is trusted: the
/// confirmation re-reads the server-side draft and consumes it exactly once.
/// </para>
/// </summary>
internal sealed record ExpensePayload
{
    private static readonly ExpensePayload Empty = new();

    public long? Amount { get; init; }

    public string? Description { get; init; }

    public Guid? CategoryId { get; init; }

    public string? CategoryName { get; init; }

    public string? CategoryIcon { get; init; }

    public DateOnly? Date { get; init; }

    /// <summary>
    /// How the category was chosen, carried to the confirmation so the expense records the
    /// suggestion quality. <c>null</c> means the picker path, which is manual.
    /// </summary>
    public CategorizationSource? Source { get; init; }

    /// <summary>
    /// The unrecognized description offered to be saved as a keyword once the user picks a
    /// category. Cleared as soon as the user decides.
    /// </summary>
    public string? LearnTerm { get; init; }

    public static ExpensePayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ExpensePayload>(payload) ?? Empty;
        }
        catch (JsonException)
        {
            // A payload written by a different version of the flow. Starting over is safer than
            // acting on a half-understood draft.
            return Empty;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);
}
