using System.Text.Json;
using System.Text.Json.Serialization;
using MyBudget.Application.Reporting;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// State for the expense list flow: which expense the detail or delete screen refers to, and
/// where the range history is standing.
/// <para>
/// Paging is keyset, so the payload carries the cursor of the page on screen and the cursor of
/// the next one, plus the stack of cursors already visited so "previous" can step back. JSON
/// because the column is <c>jsonb</c>, which PostgreSQL canonicalises; it is only ever read
/// back through this type.
/// </para>
/// </summary>
internal sealed record ExpensesPayload
{
    private static readonly ExpensesPayload Empty = new();

    public Guid? ExpenseId { get; init; }

    public DateOnly? RangeFrom { get; init; }

    public DateOnly? RangeTo { get; init; }

    public DateOnly? AfterDate { get; init; }

    public Guid? AfterId { get; init; }

    public DateOnly? NextDate { get; init; }

    public Guid? NextId { get; init; }

    public List<ExpensePageCursor> Back { get; init; } = [];

    /// <summary>The selected range, or <c>null</c> to fall back to the current month.</summary>
    [JsonIgnore]
    public DateRange? Range =>
        RangeFrom is { } from && RangeTo is { } to && to >= from ? new DateRange(from, to) : null;

    /// <summary>The keyset position the page on screen was fetched after, or the first page.</summary>
    [JsonIgnore]
    public ExpensePageCursor? After =>
        AfterDate is { } date && AfterId is { } id ? new ExpensePageCursor(date, id) : null;

    /// <summary>Where "see more" resumes, when a further page exists.</summary>
    [JsonIgnore]
    public ExpensePageCursor? Next =>
        NextDate is { } date && NextId is { } id ? new ExpensePageCursor(date, id) : null;

    public static ExpensesPayload Parse(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<ExpensesPayload>(payload) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    public ExpensesPayload WithExpense(Guid expenseId) => this with { ExpenseId = expenseId };

    /// <summary>
    /// Moves to a page: the cursor it was fetched after, the cursor of the following page, and
    /// the stack of already-visited cursors.
    /// </summary>
    public ExpensesPayload WithPage(
        ExpensePageCursor? after, ExpensePageCursor? next, IReadOnlyList<ExpensePageCursor> back) =>
        this with
        {
            AfterDate = after?.ExpenseDate,
            AfterId = after?.ExpenseId,
            NextDate = next?.ExpenseDate,
            NextId = next?.ExpenseId,
            Back = [.. back],
        };

    /// <summary>Starts a range over from its first page, dropping any paging position.</summary>
    public ExpensesPayload WithRange(DateRange range) =>
        this with
        {
            RangeFrom = range.From,
            RangeTo = range.To,
            AfterDate = null,
            AfterId = null,
            NextDate = null,
            NextId = null,
            Back = [],
        };
}
