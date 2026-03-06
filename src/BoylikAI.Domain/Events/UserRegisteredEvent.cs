using BoylikAI.Domain.Common;

namespace BoylikAI.Domain.Events;

public sealed record UserRegisteredEvent(
    Guid UserId,
    long TelegramId) : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
