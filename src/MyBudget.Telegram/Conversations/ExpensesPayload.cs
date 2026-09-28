using System.Text.Json;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// State for the expense list flow: which expense the detail or delete screen refers to.
/// JSON because the column is <c>jsonb</c>, which PostgreSQL canonicalises; it is only ever
/// read back through this type.
/// </summary>
internal sealed record ExpensesPayload
{
    private static readonly ExpensesPayload Empty = new();

    public Guid? ExpenseId { get; init; }

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
}
