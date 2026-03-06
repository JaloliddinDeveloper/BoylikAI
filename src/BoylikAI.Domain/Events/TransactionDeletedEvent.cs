using BoylikAI.Domain.Common;

namespace BoylikAI.Domain.Events;

public sealed record TransactionDeletedEvent(
    Guid TransactionId,
    Guid UserId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
