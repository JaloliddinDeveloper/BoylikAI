using BoylikAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoylikAI.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.TelegramId)
            .HasColumnName("telegram_id")
            .IsRequired();

        builder.HasIndex(u => u.TelegramId)
            .IsUnique()
            .HasDatabaseName("ix_users_telegram_id");

        builder.Property(u => u.Username)
            .HasColumnName("username")
            .HasMaxLength(128);

        builder.Property(u => u.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(128);

        builder.Property(u => u.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(128);

        builder.Property(u => u.LanguageCode)
            .HasColumnName("language_code")
            .HasMaxLength(8)
            .HasDefaultValue("uz")
            .IsRequired();

        builder.Property(u => u.DefaultCurrency)
            .HasColumnName("default_currency")
            .HasMaxLength(8)
            .HasDefaultValue("UZS")
            .IsRequired();

        builder.Property(u => u.MonthlyBudgetLimit)
            .HasColumnName("monthly_budget_limit")
            .HasPrecision(18, 2);

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true);

        builder.Property(u => u.IsNotificationsEnabled)
            .HasColumnName("is_notifications_enabled")
            .HasDefaultValue(true);

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(u => u.LastActivityAt)
            .HasColumnName("last_activity_at");

        builder.Ignore(u => u.DomainEvents);
    }
}
