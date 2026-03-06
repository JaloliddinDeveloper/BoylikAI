using BoylikAI.Domain.Common;
using BoylikAI.Domain.ValueObjects;

namespace BoylikAI.Domain.Events;

public sealed record BudgetExceededEvent(
    Guid BudgetId,
    Guid UserId,
    Money BudgetLimit,
    Money CurrentSpending) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
