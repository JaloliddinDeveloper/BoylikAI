using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoylikAI.Infrastructure.Persistence.Configurations;

public sealed class BudgetConfiguration : IEntityTypeConfiguration<Budget>
{
    public void Configure(EntityTypeBuilder<Budget> builder)
    {
        builder.ToTable("budgets");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(b => b.Category)
            .HasColumnName("category")
            .HasConversion(
                v => v.HasValue ? (int?)v.Value : null,
                v => v.HasValue ? (TransactionCategory?)v.Value : null);

        builder.OwnsOne(b => b.LimitAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("limit_amount").HasPrecision(18, 2).IsRequired();
            money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(8).IsRequired();
        });

        builder.Property(b => b.Month).HasColumnName("month").IsRequired();
        builder.Property(b => b.Year).HasColumnName("year").IsRequired();
        builder.Property(b => b.IsAlertSent).HasColumnName("is_alert_sent").HasDefaultValue(false);
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(b => new { b.UserId, b.Year, b.Month }).HasDatabaseName("ix_budgets_user_period");

        builder.HasOne(b => b.User)
            .WithMany()
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(b => b.DomainEvents);
    }
}
