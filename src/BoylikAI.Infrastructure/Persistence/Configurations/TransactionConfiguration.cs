using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoylikAI.Infrastructure.Persistence.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .HasConversion(v => (int)v, v => (TransactionType)v)
            .IsRequired();

        // Money value object owned type
        builder.OwnsOne(t => t.Amount, money =>
        {
            money.Property(m => m.Amount)
                .HasColumnName("amount")
                .HasPrecision(18, 2)
                .IsRequired();
            money.Property(m => m.Currency)
                .HasColumnName("currency")
                .HasMaxLength(8)
                .IsRequired();
        });

        builder.Property(t => t.Category)
            .HasColumnName("category")
            .HasConversion(v => (int)v, v => (TransactionCategory)v)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(t => t.OriginalMessage)
            .HasColumnName("original_message")
            .HasMaxLength(1024);

        builder.Property(t => t.TransactionDate)
            .HasColumnName("transaction_date")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(t => t.IsAiParsed)
            .HasColumnName("is_ai_parsed")
            .HasDefaultValue(false);

        builder.Property(t => t.AiConfidenceScore)
            .HasColumnName("ai_confidence_score")
            .HasPrecision(4, 3);

        builder.Property(t => t.Notes)
            .HasColumnName("notes")
            .HasMaxLength(1024);

        // Soft delete columns
        builder.Property(t => t.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(t => t.DeletedAt)
            .HasColumnName("deleted_at");

        // ── Indexes ──────────────────────────────────────────────────────────────
        // Basic lookup index
        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("ix_transactions_user_id");

        // Covering index for monthly analytics — includes is_deleted for filter efficiency
        builder.HasIndex(t => new { t.UserId, t.TransactionDate, t.IsDeleted })
            .HasDatabaseName("ix_transactions_user_date_deleted");

        // Index for category aggregation queries
        builder.HasIndex(t => new { t.UserId, t.TransactionDate, t.Type, t.Category })
            .HasDatabaseName("ix_transactions_analytics_covering");

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(t => t.DomainEvents);
    }
}
