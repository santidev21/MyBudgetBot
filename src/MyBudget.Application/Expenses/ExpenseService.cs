using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Budgets;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;

namespace MyBudget.Application.Expenses;

/// <summary>Why an expense write ended the way it did, in terms the presentation can render.</summary>
public enum ExpenseChangeStatus
{
    Saved,

    /// <summary>The expense does not exist for this user.</summary>
    NotFound,

    /// <summary>The category does not exist for this user.</summary>
    CategoryNotFound,

    /// <summary>The amount was not greater than zero.</summary>
    InvalidAmount,

    /// <summary>The date is in the future; the product tracks what was spent, not what is planned.</summary>
    InvalidDate,
}

public sealed record ExpenseChangeResult(ExpenseChangeStatus Status, Expense? Expense = null)
{
    /// <summary>
    /// Budget thresholds this write pushed a category past, highest first-run only. Empty for a
    /// failed write and for a delete, which can only lower usage.
    /// </summary>
    public IReadOnlyList<BudgetAlert> Alerts { get; init; } = [];

    public bool Saved => Status == ExpenseChangeStatus.Saved;

    public static ExpenseChangeResult Ok(
        Expense expense, IReadOnlyList<BudgetAlert>? alerts = null) =>
        new(ExpenseChangeStatus.Saved, expense) { Alerts = alerts ?? [] };

    public static ExpenseChangeResult NotFound() => new(ExpenseChangeStatus.NotFound);

    public static ExpenseChangeResult CategoryNotFound() => new(ExpenseChangeStatus.CategoryNotFound);

    public static ExpenseChangeResult InvalidAmount() => new(ExpenseChangeStatus.InvalidAmount);

    public static ExpenseChangeResult InvalidDate() => new(ExpenseChangeStatus.InvalidDate);
}

/// <summary>
/// One expense as the history and detail screens need it: the stored expense plus the category
/// label it belongs to at read time.
/// </summary>
public sealed record ExpenseListItem(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Icon,
    long Amount,
    string? Description,
    DateOnly ExpenseDate,
    CategorizationSource Source);

/// <summary>
/// Expense use cases: record, read, edit and delete.
/// <para>
/// Category ownership is checked before anything is written, so a missing scope is reported as
/// a reason rather than surfacing as a foreign key violation. Amount and date rules stay in the
/// entity; this service pre-checks them only so the presentation can show a specific message.
/// </para>
/// </summary>
public interface IExpenseService
{
    Task<ExpenseChangeResult> CreateAsync(
        Guid userId,
        Guid categoryId,
        long amount,
        string? description,
        DateOnly expenseDate,
        DateOnly today,
        CategorizationSource source = CategorizationSource.Manual,
        CancellationToken cancellationToken = default);

    Task<Expense?> GetAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default);

    /// <summary>A single expense with its category label, for the detail screen.</summary>
    Task<ExpenseListItem?> GetItemAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default);

    /// <summary>The month's expenses, newest first, with their category label.</summary>
    Task<IReadOnlyList<ExpenseListItem>> ListMonthAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default);

    Task<ExpenseChangeResult> UpdateAsync(
        Guid userId,
        Guid expenseId,
        Guid categoryId,
        long amount,
        string? description,
        DateOnly expenseDate,
        DateOnly today,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ExpenseService(
    IExpenseRepository expenses,
    ICategoryRepository categories,
    IBudgetAlertService budgetAlerts,
    IUnitOfWork unitOfWork) : IExpenseService
{
    public async Task<ExpenseChangeResult> CreateAsync(
        Guid userId,
        Guid categoryId,
        long amount,
        string? description,
        DateOnly expenseDate,
        DateOnly today,
        CategorizationSource source = CategorizationSource.Manual,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return ExpenseChangeResult.InvalidAmount();
        }

        if (expenseDate > today)
        {
            return ExpenseChangeResult.InvalidDate();
        }

        if (await categories.FindByIdAsync(userId, categoryId, cancellationToken) is null)
        {
            return ExpenseChangeResult.CategoryNotFound();
        }

        var expense = new Expense(userId, categoryId, amount, description, expenseDate, today, source);
        expenses.Add(expense);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Evaluated for the user's current month: an expense dated earlier cannot change the
        // budget they are living in today, so it never produces an alert.
        var alerts = await budgetAlerts.EvaluateAsync(
            userId, MonthPeriod.FromDate(today), cancellationToken);

        return ExpenseChangeResult.Ok(expense, alerts);
    }

    public Task<Expense?> GetAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default)
        => expenses.FindByIdAsync(userId, expenseId, cancellationToken);

    public async Task<ExpenseListItem?> GetItemAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default)
    {
        var expense = await expenses.FindByIdAsync(userId, expenseId, cancellationToken);
        if (expense is null)
        {
            return null;
        }

        var category = await categories.FindByIdAsync(userId, expense.CategoryId, cancellationToken);
        return ToItem(expense, category);
    }

    public async Task<IReadOnlyList<ExpenseListItem>> ListMonthAsync(
        Guid userId, MonthPeriod period, CancellationToken cancellationToken = default)
    {
        var monthExpenses = await expenses.ListByPeriodAsync(userId, period, cancellationToken);
        var allCategories = await categories.ListAsync(userId, includeInactive: true, cancellationToken);
        var byId = allCategories.ToDictionary(category => category.Id);

        return monthExpenses
            .Select(expense => ToItem(expense, byId.GetValueOrDefault(expense.CategoryId)))
            .ToList();
    }

    private static ExpenseListItem ToItem(Expense expense, BudgetCategory? category) =>
        new(
            expense.Id,
            expense.CategoryId,
            category?.Name ?? string.Empty,
            category?.Icon ?? string.Empty,
            expense.Amount,
            expense.Description,
            expense.ExpenseDate,
            expense.CategorizationSource);

    public async Task<ExpenseChangeResult> UpdateAsync(
        Guid userId,
        Guid expenseId,
        Guid categoryId,
        long amount,
        string? description,
        DateOnly expenseDate,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
        {
            return ExpenseChangeResult.InvalidAmount();
        }

        if (expenseDate > today)
        {
            return ExpenseChangeResult.InvalidDate();
        }

        var expense = await expenses.FindByIdAsync(userId, expenseId, cancellationToken);
        if (expense is null)
        {
            return ExpenseChangeResult.NotFound();
        }

        if (expense.CategoryId != categoryId)
        {
            if (await categories.FindByIdAsync(userId, categoryId, cancellationToken) is null)
            {
                return ExpenseChangeResult.CategoryNotFound();
            }

            // The user picked the category explicitly, so the original suggestion no longer
            // describes how it was chosen.
            expense.ChangeCategory(categoryId, CategorizationSource.Manual);
        }

        expense.ChangeAmount(amount);
        expense.ChangeDescription(description);
        expense.ChangeDate(expenseDate, today);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var alerts = await budgetAlerts.EvaluateAsync(
            userId, MonthPeriod.FromDate(today), cancellationToken);

        return ExpenseChangeResult.Ok(expense, alerts);
    }

    public async Task<bool> DeleteAsync(
        Guid userId, Guid expenseId, CancellationToken cancellationToken = default)
    {
        var expense = await expenses.FindByIdAsync(userId, expenseId, cancellationToken);
        if (expense is null)
        {
            return false;
        }

        expenses.Remove(expense);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
