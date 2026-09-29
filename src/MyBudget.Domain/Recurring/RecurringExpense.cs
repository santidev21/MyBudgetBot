using MyBudget.Domain.Common;

namespace MyBudget.Domain.Recurring;

/// <summary>
/// A monthly expense the user pre-authorised: rent, subscriptions, utilities.
/// <para>
/// The rule is configuration, not history. When the due date arrives the scheduler creates a
/// real <c>Expense</c> dated that calendar day; the rule only remembers up to which date it has
/// already generated. Deleting a rule never touches the expenses it produced.
/// </para>
/// <para>
/// Monthly only for now. Weekly and yearly recurrences would need their own anchor rules; the
/// day-of-month + clamping below is what the product actually needs.
/// </para>
/// </summary>
public sealed class RecurringExpense : Entity
{
    public const int MinDayOfMonth = 1;
    public const int MaxDayOfMonth = 31;

    /// <summary>Bound on how many missed occurrences one run may catch up on.</summary>
    public const int MaxOccurrencesPerRun = 24;

    private RecurringExpense()
    {
    }

    public RecurringExpense(
        Guid userId,
        Guid categoryId,
        long amount,
        string? description,
        int dayOfMonth,
        DateOnly startDate,
        DateOnly? endDate = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        UserId = userId;
        ChangeCategory(categoryId);
        ChangeAmount(amount);
        ChangeDescription(description);
        ChangeDayOfMonth(dayOfMonth);
        ChangePeriod(startDate, endDate);
        IsActive = true;
    }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

    public long Amount { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Day of the month it falls due. A shorter month clamps to its last day.</summary>
    public int DayOfMonth { get; private set; }

    /// <summary>First month the rule applies. An occurrence before this date is skipped.</summary>
    public DateOnly StartDate { get; private set; }

    /// <summary>Inclusive last date, or <c>null</c> for "until the user stops it".</summary>
    public DateOnly? EndDate { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The most recent occurrence already turned into an expense.</summary>
    public DateOnly? LastGeneratedDate { get; private set; }

    /// <summary>
    /// Every occurrence due on or before <paramref name="today"/> that has not been generated
    /// yet, oldest first. Pure: it never writes, so the rule can be asked and left untouched.
    /// </summary>
    public IReadOnlyList<DateOnly> DueDates(DateOnly today, int maxOccurrences = MaxOccurrencesPerRun)
    {
        if (!IsActive || maxOccurrences <= 0)
        {
            return [];
        }

        var dates = new List<DateOnly>();

        // Start from the month of the last generation, or of the start date for a fresh rule.
        var anchor = LastGeneratedDate ?? StartDate;
        var month = new DateOnly(anchor.Year, anchor.Month, 1);

        // 100 years of months: unreachable in practice, and it keeps a corrupt date from
        // turning into an infinite loop.
        for (var guard = 0; dates.Count < maxOccurrences && guard < 1_200; guard++)
        {
            var occurrence = InMonth(month.Year, month.Month, DayOfMonth);

            if (occurrence > today)
            {
                break;
            }

            if (EndDate is { } end && occurrence > end)
            {
                break;
            }

            var afterStart = occurrence >= StartDate;
            var afterLast = LastGeneratedDate is not { } last || occurrence > last;

            if (afterStart && afterLast)
            {
                dates.Add(occurrence);
            }

            month = month.AddMonths(1);
        }

        return dates;
    }

    /// <summary>Records that an occurrence was turned into an expense. Must move forward.</summary>
    public void RecordGeneration(DateOnly date)
    {
        if (LastGeneratedDate is { } last && date <= last)
        {
            throw new InvalidOperationException(
                $"Generation date {date} must be later than the last generated date {last}.");
        }

        LastGeneratedDate = date;
    }

    public void ChangeCategory(Guid categoryId)
    {
        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("Category id is required.", nameof(categoryId));
        }

        CategoryId = categoryId;
    }

    public void ChangeAmount(long amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "A recurring expense must be greater than zero.");
        }

        if (amount > MoneyLimits.MaxAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, $"A recurring expense cannot exceed {MoneyLimits.MaxAmount}.");
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
        if (trimmed.Length > Expenses.Expense.MaxDescriptionLength)
        {
            throw new ArgumentException(
                $"Description cannot exceed {Expenses.Expense.MaxDescriptionLength} characters.",
                nameof(description));
        }

        Description = trimmed;
    }

    public void ChangeDayOfMonth(int dayOfMonth)
    {
        if (dayOfMonth is < MinDayOfMonth or > MaxDayOfMonth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayOfMonth), dayOfMonth,
                $"Day of month must be between {MinDayOfMonth} and {MaxDayOfMonth}.");
        }

        DayOfMonth = dayOfMonth;
    }

    public void ChangePeriod(DateOnly startDate, DateOnly? endDate)
    {
        if (endDate is { } end && end < startDate)
        {
            throw new ArgumentException("The end date cannot be before the start date.", nameof(endDate));
        }

        StartDate = startDate;
        EndDate = endDate;
    }

    public void SetActive(bool active) => IsActive = active;

    /// <summary>The occurrence of a rule in a month, clamped to the month's last day.</summary>
    public static DateOnly InMonth(int year, int month, int dayOfMonth) =>
        new(year, month, Math.Min(dayOfMonth, DateTime.DaysInMonth(year, month)));
}
