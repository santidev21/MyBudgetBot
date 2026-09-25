using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", table =>
        {
            table.HasCheckConstraint("ck_users_currency", "currency ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_users_language", "language ~ '^[a-z]{2}(-[A-Z]{2})?$'");
        });

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.TelegramUserId).HasColumnName("telegram_user_id").IsRequired();
        builder.Property(user => user.Username).HasColumnName("username").HasMaxLength(64);
        builder.Property(user => user.DisplayName).HasColumnName("display_name").HasMaxLength(128);
        builder.Property(user => user.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(user => user.TimeZone).HasColumnName("time_zone").HasMaxLength(64).IsRequired();
        builder.Property(user => user.Language).HasColumnName("language").HasMaxLength(5).IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(user => user.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder
            .HasIndex(user => user.TelegramUserId)
            .IsUnique()
            .HasDatabaseName("uq_users_telegram_user_id");
    }
}
