using BoylikAI.Domain.Common;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Events;
using BoylikAI.Domain.ValueObjects;

namespace BoylikAI.Domain.Entities;

public sealed class Transaction : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public TransactionType Type { get; private set; }
    public Money Amount { get; private set; } = Money.Zero;
    public TransactionCategory Category { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? OriginalMessage { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool IsAiParsed { get; private set; }
    public decimal? AiConfidenceScore { get; private set; }
    public string? Notes { get; private set; }

    // Soft delete — moliyaviy ma'lumotlar hech qachon hard delete qilinmasligi kerak
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public User? User { get; private set; }

    private Transaction() { }

    public static Transaction Create(
        Guid userId,
        TransactionType type,
        decimal amount,
        string currency,
        TransactionCategory category,
        string description,
        DateOnly transactionDate,
        string? originalMessage = null,
        bool isAiParsed = false,
        decimal? aiConfidenceScore = null)
    {
        if (amount <= 0)
            throw new ArgumentException("Transaction amount must be positive", nameof(amount));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty", nameof(description));

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = type,
            Amount = new Money(amount, currency),
            Category = category,
            Description = description.Trim(),
            TransactionDate = transactionDate,
            OriginalMessage = originalMessage,
            IsAiParsed = isAiParsed,
            AiConfidenceScore = aiConfidenceScore,
            IsDeleted = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        transaction.RaiseDomainEvent(new TransactionCreatedEvent(
            transaction.Id, userId, type, new Money(amount, currency), category));

        return transaction;
    }

    public void Update(
        decimal amount,
        string currency,
        TransactionCategory category,
        string description,
        DateOnly transactionDate)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive", nameof(amount));

        Amount = new Money(amount, currency);
        Category = category;
        Description = description.Trim();
        TransactionDate = transactionDate;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SoftDelete()
    {
        if (IsDeleted) return;
        IsDeleted = true;
        DeletedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new TransactionDeletedEvent(Id, UserId));
    }

    public void AddNotes(string notes)
    {
        Notes = notes?.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
