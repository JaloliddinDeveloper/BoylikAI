using BoylikAI.Domain.Common;
using BoylikAI.Domain.Events;

namespace BoylikAI.Domain.Entities;

public sealed class User : Entity<Guid>
{
    public long TelegramId { get; private set; }
    public string? Username { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string LanguageCode { get; private set; } = "uz";
    public string DefaultCurrency { get; private set; } = "UZS";
    public decimal? MonthlyBudgetLimit { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsNotificationsEnabled { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }

    // NOTE: Transactions navigation property o'chirildi.
    // Tranzaksiyalar ITransactionRepository orqali olinadi.
    // EF Core lazy loading yoqilmagan, shuning uchun bu xossa
    // har doim bo'sh list qaytarardi va developer'ni chalg'itardi.

    private User() { }

    public static User Create(
        long telegramId,
        string? username,
        string? firstName,
        string? lastName,
        string languageCode = "uz")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            TelegramId = telegramId,
            Username = username?.Trim(),
            FirstName = firstName?.Trim(),
            LastName = lastName?.Trim(),
            LanguageCode = languageCode,
            DefaultCurrency = "UZS",
            IsActive = true,
            IsNotificationsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        user.RaiseDomainEvent(new UserRegisteredEvent(user.Id, telegramId));
        return user;
    }

    public void SetBudgetLimit(decimal limit)
    {
        if (limit < 0)
            throw new ArgumentException("Budget limit cannot be negative", nameof(limit));
        MonthlyBudgetLimit = limit;
    }

    public void UpdateLastActivity() => LastActivityAt = DateTimeOffset.UtcNow;

    public void UpdatePreferences(string languageCode, string currency)
    {
        if (!string.IsNullOrWhiteSpace(languageCode))
            LanguageCode = languageCode;
        if (!string.IsNullOrWhiteSpace(currency))
            DefaultCurrency = currency;
    }

    public void Deactivate() => IsActive = false;

    /// <summary>GDPR "right to be forgotten" — PII ma'lumotlarini tozalash.</summary>
    public void Anonymize()
    {
        Username = null;
        FirstName = "[Deleted]";
        LastName = null;
        IsActive = false;
    }

    public string GetDisplayName() =>
        FirstName is not null
            ? $"{FirstName} {LastName}".Trim()
            : Username ?? $"User#{TelegramId}";
}
