using BoylikAI.Domain.Common;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.ValueObjects;

namespace BoylikAI.Domain.Events;

public sealed record TransactionCreatedEvent(
    Guid TransactionId,
    Guid UserId,
    TransactionType Type,
    Money Amount,
    TransactionCategory Category) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
