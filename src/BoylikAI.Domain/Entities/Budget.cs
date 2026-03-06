using BoylikAI.Domain.Common;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Events;
using BoylikAI.Domain.ValueObjects;

namespace BoylikAI.Domain.Entities;

public sealed class Budget : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public TransactionCategory? Category { get; private set; }
    public Money LimitAmount { get; private set; } = Money.Zero;
    public int Month { get; private set; }
    public int Year { get; private set; }
    public bool IsAlertSent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public User? User { get; private set; }

    private Budget() { }

    public static Budget Create(
        Guid userId,
        decimal limit,
        string currency,
        int month,
        int year,
        TransactionCategory? category = null)
    {
        if (limit <= 0)
            throw new ArgumentException("Budget limit must be positive", nameof(limit));

        return new Budget
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Category = category,
            LimitAmount = new Money(limit, currency),
            Month = month,
            Year = year,
            IsAlertSent = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void CheckAndAlert(Money currentSpending)
    {
        if (!IsAlertSent && currentSpending.Amount >= LimitAmount.Amount * 0.8m)
        {
            IsAlertSent = true;
            RaiseDomainEvent(new BudgetExceededEvent(Id, UserId, LimitAmount, currentSpending));
        }
    }

    public decimal GetUsagePercentage(Money currentSpending) =>
        LimitAmount.Amount == 0
            ? 0
            : Math.Round((currentSpending.Amount / LimitAmount.Amount) * 100, 2);
}
