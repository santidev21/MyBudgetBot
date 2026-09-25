using Microsoft.EntityFrameworkCore;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Persistence;

public sealed class MyBudgetDbContext(DbContextOptions<MyBudgetDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<BudgetCategory> Categories => Set<BudgetCategory>();

    public DbSet<CategoryAlias> CategoryAliases => Set<CategoryAlias>();

    public DbSet<MonthlyBudget> MonthlyBudgets => Set<MonthlyBudget>();

    public DbSet<MonthlyBudgetCategory> MonthlyBudgetCategories => Set<MonthlyBudgetCategory>();

    public DbSet<Expense> Expenses => Set<Expense>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MyBudgetDbContext).Assembly);
    }
}
