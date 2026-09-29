using System.Text.Json;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The draft a category flow carries between steps, persisted as the conversation payload.
/// <para>
/// It is JSON because the column is <c>jsonb</c>; PostgreSQL canonicalises it, so it is only
/// ever read back through this type and never compared as a string. Nothing here is trusted:
/// the category is always re-read and ownership-checked before it is used.
/// </para>
/// </summary>
internal sealed record CategoriesPayload
{
    private static readonly CategoriesPayload Empty = new();

    public Guid? CategoryId { get; init; }

    /// <summary>A category name waiting for its icon.</summary>
    public string? Name { get; init; }

    /// <summary>An amount waiting for the month/all-months scope decision.</summary>
    public long? Amount { get; init; }

    /// <summary>A keyword waiting for the conflict decision.</summary>
    public string? Alias { get; init; }

    /// <summary>
    /// The keywords as they were shown, so a remove button can be matched to a term by position
    /// without putting a 60 character alias in callback data, which Telegram caps at 64 bytes.
    /// </summary>
    public IReadOnlyList<string>? Aliases { get; init; }

    public static CategoriesPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<CategoriesPayload>(payload) ?? Empty;
        }
        catch (JsonException)
        {
            // A payload this code cannot read means it was written by a different version of
            // the flow. Starting it over is safer than acting on a half-understood draft.
            return Empty;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    public CategoriesPayload WithCategory(Guid categoryId) => this with { CategoryId = categoryId };
}
