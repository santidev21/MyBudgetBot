using MyBudget.Domain.Common;

namespace MyBudget.Domain.Expenses;

/// <summary>
/// A single expense. <see cref="Amount"/> is a whole number of COP pesos: 35.000 COP is 35000.
/// <see cref="ExpenseDate"/> is a calendar date in the user's local time zone, never a UTC
/// instant, so month boundaries and historical reports stay correct.
/// </summary>
public sealed class Expense : Entity
{
    /// <summary>Upper bound that also exists as a database CHECK constraint.</summary>
    public const long MaxAmount = 999_999_999_999L;

    public const int MaxDescriptionLength = 500;

    private Expense()
    {
    }

    public Expense(
        Guid userId,
        Guid categoryId,
        long amount,
        string? description,
        DateOnly expenseDate,
        DateOnly today,
        CategorizationSource source = CategorizationSource.Manual)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        UserId = userId;
        ChangeCategory(categoryId, source);
        ChangeAmount(amount);
        ChangeDescription(description);
        ChangeDate(expenseDate, today);
    }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

    public long Amount { get; private set; }

    public string? Description { get; private set; }

    public DateOnly ExpenseDate { get; private set; }

    public CategorizationSource CategorizationSource { get; private set; } = CategorizationSource.Manual;

    public void ChangeAmount(long amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "An expense must be greater than zero.");
        }

        if (amount > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, $"An expense cannot exceed {MaxAmount}.");
        }

        Amount = amount;
    }

    public void ChangeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            Description = null;
            return;
        }

        var trimmed = description.Trim();
        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Description cannot exceed {MaxDescriptionLength} characters.", nameof(description));
        }

        Description = trimmed;
    }

    public void ChangeCategory(Guid categoryId, CategorizationSource source = CategorizationSource.Manual)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("Category id is required.", nameof(categoryId));
        }

        CategoryId = categoryId;
        CategorizationSource = source;
    }

    /// <summary>
    /// Moves the expense to another calendar date. Future dates are rejected:
    /// the product tracks what was spent, not what is planned.
    /// </summary>
    public void ChangeDate(DateOnly expenseDate, DateOnly today)
    {
        if (expenseDate > today)
        {
            throw new ArgumentException(
                "An expense cannot be dated in the future.", nameof(expenseDate));
        }

        ExpenseDate = expenseDate;
    }
}
