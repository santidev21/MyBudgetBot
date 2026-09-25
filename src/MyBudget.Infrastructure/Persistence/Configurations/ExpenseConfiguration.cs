using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBudget.Domain.Expenses;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Persistence.Configurations;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("expenses", table =>
        {
            table.HasCheckConstraint(
                "ck_expenses_amount",
                "amount > 0 AND amount <= 999999999999");
            table.HasCheckConstraint(
                "ck_expenses_description",
                "description IS NULL OR char_length(description) <= 500");
            table.HasCheckConstraint(
                "ck_expenses_source",
                "categorization_source IN ('manual','matched','ambiguous','learned')");
        });

        builder.HasKey(expense => expense.Id);

        builder.Property(expense => expense.Id).HasColumnName("id");
        builder.Property(expense => expense.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(expense => expense.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(expense => expense.Amount).HasColumnName("amount").HasColumnType("bigint").IsRequired();
        builder.Property(expense => expense.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(expense => expense.ExpenseDate).HasColumnName("expense_date").HasColumnType("date").IsRequired();
        builder.Property(expense => expense.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(expense => expense.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder
            .Property(expense => expense.CategorizationSource)
            .HasColumnName("categorization_source")
            .HasConversion(
                source => source.ToString().ToLowerInvariant(),
                value => Enum.Parse<CategorizationSource>(value, true))
            .HasMaxLength(16)
            .IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(expense => expense.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Newest first, used by the paginated expense history.
        builder
            .HasIndex(expense => new { expense.UserId, expense.ExpenseDate, expense.Id })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_expenses_user_date");

        // Supports per-category aggregation for a period.
        builder
            .HasIndex(expense => new { expense.UserId, expense.CategoryId, expense.ExpenseDate })
            .HasDatabaseName("ix_expenses_user_category_date");

        // The same-user guarantee for the category reference is a composite foreign key
        // created by the InitialSchema migration:
        //   (category_id, user_id) -> categories (id, user_id)
    }
}
