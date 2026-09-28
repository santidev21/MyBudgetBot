using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyBudget.Domain.Users;
using MyBudget.Infrastructure.Persistence.Records;

namespace MyBudget.Infrastructure.Persistence.Configurations;

internal sealed class TelegramUpdateRecordConfiguration : IEntityTypeConfiguration<TelegramUpdateRecord>
{
    public void Configure(EntityTypeBuilder<TelegramUpdateRecord> builder)
    {
        builder.ToTable("telegram_updates", table => table.HasCheckConstraint(
            "ck_telegram_updates_status",
            "status IN ('received','processed','failed','ignored')"));

        builder.HasKey(record => record.UpdateId);

        // The key comes from Telegram, so it is never generated here.
        builder.Property(record => record.UpdateId)
            .HasColumnName("update_id")
            .ValueGeneratedNever();
        builder.Property(record => record.UserId).HasColumnName("user_id");
        builder.Property(record => record.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(record => record.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamptz");
        builder.Property(record => record.Status).HasColumnName("status").HasMaxLength(16).IsRequired();
        builder.Property(record => record.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(record => record.LastError).HasColumnName("last_error").HasMaxLength(500);

        builder
            .HasIndex(record => record.ReceivedAt)
            .HasDatabaseName("ix_telegram_updates_received_at");

        // Deleting a user must not lose the operational trail of what Telegram delivered.
        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ConversationStateRecordConfiguration : IEntityTypeConfiguration<ConversationStateRecord>
{
    public void Configure(EntityTypeBuilder<ConversationStateRecord> builder)
    {
        builder.ToTable("conversation_states", table => table.HasCheckConstraint(
            "ck_conversation_states_non_empty",
            "btrim(conversation) <> '' AND btrim(state) <> ''"));

        builder.HasKey(record => record.UserId);

        builder.Property(record => record.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(record => record.ChatId).HasColumnName("chat_id").IsRequired();
        builder.Property(record => record.Conversation).HasColumnName("conversation").HasMaxLength(64).IsRequired();
        builder.Property(record => record.State).HasColumnName("state").HasMaxLength(64).IsRequired();
        builder.Property(record => record.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(record => record.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(record => record.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(record => record.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
