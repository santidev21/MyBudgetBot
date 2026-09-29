using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Persistence.Configurations;

internal sealed class BudgetDefaultConfiguration : IEntityTypeConfiguration<BudgetDefault>
{
    public void Configure(EntityTypeBuilder<BudgetDefault> builder)
    {
        builder.ToTable("budget_defaults", table =>
        {
            table.HasCheckConstraint("ck_budget_defaults_amount", "amount >= 0");
            table.HasCheckConstraint("ck_budget_defaults_month", "effective_from_month BETWEEN 1 AND 12");
            table.HasCheckConstraint("ck_budget_defaults_year", "effective_from_year BETWEEN 2000 AND 2100");
        });

        builder.HasKey(budgetDefault => budgetDefault.Id);

        builder.Property(budgetDefault => budgetDefault.Id).HasColumnName("id");
        builder.Property(budgetDefault => budgetDefault.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(budgetDefault => budgetDefault.CategoryId).HasColumnName("category_id").IsRequired();
        builder.Property(budgetDefault => budgetDefault.EffectiveFromYear)
            .HasColumnName("effective_from_year").HasColumnType("smallint").IsRequired();
        builder.Property(budgetDefault => budgetDefault.EffectiveFromMonth)
            .HasColumnName("effective_from_month").HasColumnType("smallint").IsRequired();
        builder.Property(budgetDefault => budgetDefault.Amount).HasColumnName("amount").HasColumnType("bigint").IsRequired();
        builder.Property(budgetDefault => budgetDefault.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(budgetDefault => budgetDefault.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder
            .HasIndex(budgetDefault => new { budgetDefault.UserId, budgetDefault.CategoryId })
            .IsUnique()
            .HasDatabaseName("uq_budget_defaults_user_category");

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(budgetDefault => budgetDefault.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The same-user guarantee for the category reference is a composite foreign key created
        // by the BudgetDefaults migration:
        //   (category_id, user_id) -> categories (id, user_id)
    }
}
