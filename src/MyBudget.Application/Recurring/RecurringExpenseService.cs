using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Budgets;
using MyBudget.Application.Dates;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using MyBudget.Domain.Recurring;
using MyBudget.Domain.Users;

namespace MyBudget.Application.Recurring;

/// <summary>Why a recurring-rule write ended the way it did, in terms the presentation renders.</summary>
public enum RecurringChangeStatus
{
    Saved,

    /// <summary>The rule does not exist for this user.</summary>
    NotFound,

    /// <summary>The category does not exist for this user.</summary>
    CategoryNotFound,

    /// <summary>The amount was not greater than zero.</summary>
    InvalidAmount,

    /// <summary>The day of the month was outside 1..31.</summary>
    InvalidDay,

    /// <summary>The end date was before the start date.</summary>
    InvalidPeriod,
}

public sealed record RecurringChangeResult(RecurringChangeStatus Status, RecurringExpense? Rule = null)
{
    public bool Saved => Status == RecurringChangeStatus.Saved;

    public static RecurringChangeResult Ok(RecurringExpense rule) => new(RecurringChangeStatus.Saved, rule);

    public static RecurringChangeResult NotFound() => new(RecurringChangeStatus.NotFound);

    public static RecurringChangeResult CategoryNotFound() => new(RecurringChangeStatus.CategoryNotFound);

    public static RecurringChangeResult InvalidAmount() => new(RecurringChangeStatus.InvalidAmount);

    public static RecurringChangeResult InvalidDay() => new(RecurringChangeStatus.InvalidDay);

    public static RecurringChangeResult InvalidPeriod() => new(RecurringChangeStatus.InvalidPeriod);
}

/// <summary>A recurring rule with the category label it belongs to at read time.</summary>
public sealed record RecurringExpenseView(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Amount,
    string? Description,
    int DayOfMonth,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool IsActive,
    DateOnly? LastGeneratedDate);

/// <summary>One expense the scheduler created from a rule.</summary>
public sealed record GeneratedRecurringExpense(
    Guid RecurringExpenseId,
    Guid ExpenseId,
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Amount,
    string? Description,
    DateOnly Date);

/// <summary>The expenses one run created for one user, and the user to tell about them.</summary>
public sealed record RecurringApplicationResult(
    User User,
    IReadOnlyList<GeneratedRecurringExpense> Generated)
{
    /// <summary>Budget thresholds this run pushed a category past, if any.</summary>
    public IReadOnlyList<BudgetAlert> Alerts { get; init; } = [];
}

/// <summary>
/// Recurring rule use cases, plus the nightly application pass.
/// <para>
/// A rule is configuration: creating, pausing and deleting it never rewrites an expense. The
/// scheduler materialises real expenses, dated in the user's own calendar, and remembers how
/// far it got so a re-run cannot duplicate them.
/// </para>
/// </summary>
public interface IRecurringExpenseService
{
    Task<IReadOnlyList<RecurringExpenseView>> ListAsync(
        Guid userId, CancellationToken cancellationToken = default);

    Task<RecurringExpenseView?> GetViewAsync(
        Guid userId, Guid recurringExpenseId, CancellationToken cancellationToken = default);

    Task<RecurringChangeResult> CreateAsync(
        Guid userId,
        Guid categoryId,
        long amount,
        string? description,
        int dayOfMonth,
        DateOnly startDate,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default);

    Task<RecurringChangeResult> SetActiveAsync(
        Guid userId, Guid recurringExpenseId, bool active, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid userId, Guid recurringExpenseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns every due occurrence into an expense, for every user, in one pass. Returns one
    /// entry per user that got at least one expense, so the caller can notify them.
    /// </summary>
    Task<IReadOnlyList<RecurringApplicationResult>> ApplyDueAsync(
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class RecurringExpenseService(
    IRecurringExpenseRepository recurring,
    IExpenseRepository expenses,
    ICategoryRepository categories,
    IUserRepository users,
    IBudgetAlertService budgetAlerts,
    IUserLocalDate localDate,
    IUnitOfWork unitOfWork) : IRecurringExpenseService
{
    public async Task<IReadOnlyList<RecurringExpenseView>> ListAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var rules = await recurring.ListAsync(userId, cancellationToken);
        var allCategories = await categories.ListAsync(userId, includeInactive: true, cancellationToken);
        var byId = allCategories.ToDictionary(category => category.Id);

        return rules.Select(rule => ToView(rule, byId.GetValueOrDefault(rule.CategoryId))).ToList();
    }

    public async Task<RecurringExpenseView?> GetViewAsync(
        Guid userId, Guid recurringExpenseId, CancellationToken cancellationToken = default)
    {
        var rule = await recurring.FindByIdAsync(userId, recurringExpenseId, cancellationToken);
        if (rule is null)
        {
            return null;
        }

        var category = await categories.FindByIdAsync(userId, rule.CategoryId, cancellationToken);
        return ToView(rule, category);
    }

    private static RecurringExpenseView ToView(RecurringExpense rule, BudgetCategory? category) =>
        new(
            rule.Id,
            rule.CategoryId,
            category?.Name ?? string.Empty,
            category?.Icon ?? string.Empty,
            rule.Amount,
            rule.Description,
            rule.DayOfMonth,
            rule.StartDate,
            rule.EndDate,
            rule.IsActive,
            rule.LastGeneratedDate);

    public async Task<RecurringChangeResult> CreateAsync(
        Guid userId,
        Guid categoryId,
        long amount,
        string? description,
        int dayOfMonth,
        DateOnly startDate,
        DateOnly? endDate = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return RecurringChangeResult.InvalidAmount();
        }

        if (dayOfMonth is < RecurringExpense.MinDayOfMonth or > RecurringExpense.MaxDayOfMonth)
        {
            return RecurringChangeResult.InvalidDay();
        }

        if (endDate is { } end && end < startDate)
        {
            return RecurringChangeResult.InvalidPeriod();
        }

        if (await categories.FindByIdAsync(userId, categoryId, cancellationToken) is null)
        {
            return RecurringChangeResult.CategoryNotFound();
        }

        var rule = new RecurringExpense(userId, categoryId, amount, description, dayOfMonth, startDate, endDate);
        recurring.Add(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RecurringChangeResult.Ok(rule);
    }

    public async Task<RecurringChangeResult> SetActiveAsync(
        Guid userId, Guid recurringExpenseId, bool active, CancellationToken cancellationToken = default)
    {
        var rule = await recurring.FindByIdAsync(userId, recurringExpenseId, cancellationToken);
        if (rule is null)
        {
            return RecurringChangeResult.NotFound();
        }

        rule.SetActive(active);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return RecurringChangeResult.Ok(rule);
    }

    public async Task<bool> DeleteAsync(
        Guid userId, Guid recurringExpenseId, CancellationToken cancellationToken = default)
    {
        var rule = await recurring.FindByIdAsync(userId, recurringExpenseId, cancellationToken);
        if (rule is null)
        {
            return false;
        }

        recurring.Remove(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<RecurringApplicationResult>> ApplyDueAsync(
        CancellationToken cancellationToken = default)
    {
        var due = await recurring.ListActiveAsync(cancellationToken);
        if (due.Count == 0)
        {
            return [];
        }

        var results = new List<RecurringApplicationResult>();

        foreach (var group in due.GroupBy(rule => rule.UserId))
        {
            var user = await users.FindByIdAsync(group.Key, cancellationToken);
            if (user is null)
            {
                // The foreign key makes this unreachable; skip rather than fail the whole run.
                continue;
            }

            // The user's calendar date, not UTC: a rule due on the 1st must land on the 1st
            // in Bogotá even when the run happens late on the 30th in UTC.
            var today = localDate.Today(user.TimeZone);
            var allCategories = await categories.ListAsync(user.Id, includeInactive: true, cancellationToken);
            var byId = allCategories.ToDictionary(category => category.Id);
            var generated = new List<GeneratedRecurringExpense>();

            foreach (var rule in group)
            {
                var category = byId.GetValueOrDefault(rule.CategoryId);

                foreach (var date in rule.DueDates(today))
                {
                    var expense = new Expense(
                        user.Id,
                        rule.CategoryId,
                        rule.Amount,
                        rule.Description,
                        date,
                        today,
                        CategorizationSource.Manual);

                    expenses.Add(expense);
                    rule.RecordGeneration(date);
                    generated.Add(new GeneratedRecurringExpense(
                        rule.Id,
                        expense.Id,
                        rule.CategoryId,
                        category?.Name ?? string.Empty,
                        category?.Icon ?? string.Empty,
                        rule.Amount,
                        rule.Description,
                        date));
                }
            }

            if (generated.Count > 0)
            {
                results.Add(new RecurringApplicationResult(user, generated));
            }
        }

        // One transaction for the whole pass: either every occurrence is recorded, or none is
        // and the next run retries. RecordGeneration and the expense insert commit together,
        // which is what keeps the pass idempotent.
        if (results.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Evaluated after the commit so the new expenses are already part of the month's usage.
        var withAlerts = new List<RecurringApplicationResult>(results.Count);

        foreach (var result in results)
        {
            var today = localDate.Today(result.User.TimeZone);
            var alerts = await budgetAlerts.EvaluateAsync(
                result.User.Id, MonthPeriod.FromDate(today), cancellationToken);

            withAlerts.Add(result with { Alerts = alerts });
        }

        return withAlerts;
    }
}
