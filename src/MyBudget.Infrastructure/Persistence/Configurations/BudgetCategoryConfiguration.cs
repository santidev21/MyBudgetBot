using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Persistence.Configurations;

internal sealed class BudgetCategoryConfiguration : IEntityTypeConfiguration<BudgetCategory>
{
    public void Configure(EntityTypeBuilder<BudgetCategory> builder)
    {
        builder.ToTable("categories", table =>
        {
            table.HasCheckConstraint(
                "ck_categories_name",
                "btrim(name) <> '' AND char_length(name) <= 60");
            table.HasCheckConstraint("ck_categories_icon", "char_length(icon) <= 16");
        });

        builder.HasKey(category => category.Id);

        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(category => category.Name).HasColumnName("name").HasMaxLength(60).IsRequired();
        builder.Property(category => category.Icon).HasColumnName("icon").HasMaxLength(16).IsRequired();
        builder.Property(category => category.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(category => category.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(category => category.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(category => category.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(category => category.Aliases)
            .WithOne()
            .HasForeignKey(alias => alias.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .Navigation(category => category.Aliases)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
