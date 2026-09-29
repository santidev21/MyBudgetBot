using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBudget.Domain.Recurring;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Persistence.Configurations;

internal sealed class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.ToTable("recurring_expenses", table =>
        {
            table.HasCheckConstraint(
                "ck_recurring_expenses_amount",
                "amount > 0 AND amount <= 999999999999");
            table.HasCheckConstraint(
                "ck_recurring_expenses_description",
                "description IS NULL OR char_length(description) <= 500");
            table.HasCheckConstraint(
                "ck_recurring_expenses_day",
                "day_of_month BETWEEN 1 AND 31");
            table.HasCheckConstraint(
                "ck_recurring_expenses_period",
                "end_date IS NULL OR end_date >= start_date");
        });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id).HasColumnName("id");
        builder.Property(rule => rule.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(rule => rule.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(rule => rule.Amount).HasColumnName("amount").HasColumnType("bigint").IsRequired();
        builder.Property(rule => rule.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(rule => rule.DayOfMonth).HasColumnName("day_of_month").HasColumnType("smallint").IsRequired();
        builder.Property(rule => rule.StartDate).HasColumnName("start_date").HasColumnType("date").IsRequired();
        builder.Property(rule => rule.EndDate).HasColumnName("end_date").HasColumnType("date");
        builder.Property(rule => rule.IsActive).HasColumnName("is_active").HasColumnType("boolean").IsRequired();
        builder.Property(rule => rule.LastGeneratedDate).HasColumnName("last_generated_date").HasColumnType("date");
        builder.Property(rule => rule.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(rule => rule.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(rule => rule.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The scheduler looks for active rules across all users.
        builder
            .HasIndex(rule => new { rule.UserId, rule.IsActive })
            .HasDatabaseName("ix_recurring_expenses_user_active");

        // The same-user guarantee for the category reference is a composite foreign key
        // created by the RecurringExpenses migration:
        //   (category_id, user_id) -> categories (id, user_id)
    }
}
