using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Persistence.Configurations;

internal sealed class MonthlyBudgetConfiguration : IEntityTypeConfiguration<MonthlyBudget>
{
    public void Configure(EntityTypeBuilder<MonthlyBudget> builder)
    {
        builder.ToTable("monthly_budgets", table =>
        {
            table.HasCheckConstraint("ck_monthly_budgets_month", "month BETWEEN 1 AND 12");
            table.HasCheckConstraint("ck_monthly_budgets_year", "year BETWEEN 2000 AND 2100");
        });

        builder.HasKey(budget => budget.Id);

        builder.Property(budget => budget.Id).HasColumnName("id");
        builder.Property(budget => budget.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(budget => budget.Year).HasColumnName("year").HasColumnType("smallint").IsRequired();
        builder.Property(budget => budget.Month).HasColumnName("month").HasColumnType("smallint").IsRequired();
        builder.Property(budget => budget.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(budget => budget.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(budget => budget.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(budget => new { budget.UserId, budget.Year, budget.Month })
            .IsUnique()
            .HasDatabaseName("uq_monthly_budgets_period");

        builder
            .HasMany(budget => budget.Allocations)
            .WithOne()
            .HasForeignKey(allocation => allocation.MonthlyBudgetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .Navigation(budget => budget.Allocations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
