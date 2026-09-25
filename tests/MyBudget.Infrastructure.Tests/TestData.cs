using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Tests;

internal static class TestData
{
    /// <summary>Reference "now" for the tests. Late enough that every dated sample is in the past.</summary>
    public static readonly DateOnly Today = new(2026, 12, 31);

    /// <summary>Default expense date, inside September 2026 so period queries have data.</summary>
    public static readonly DateOnly DefaultExpenseDate = new(2026, 9, 15);

    public static User NewUser(long telegramUserId = 1) =>
        new(telegramUserId, $"user{telegramUserId}", $"User {telegramUserId}");

    public static BudgetCategory NewCategory(Guid userId, string name = "Mercado", string icon = "🛒") =>
        new(userId, name, icon);

    public static MonthlyBudget NewBudget(Guid userId, int year = 2026, int month = 9) =>
        new(userId, new MonthPeriod(year, month));

    public static Expense NewExpense(
        Guid userId,
        Guid categoryId,
        long amount = 35_000,
        string? description = "Verduras",
        DateOnly? date = null) =>
        new(userId, categoryId, amount, description, date ?? DefaultExpenseDate, Today);
}
