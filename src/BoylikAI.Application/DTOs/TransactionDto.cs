using BoylikAI.Domain.Enums;

namespace BoylikAI.Application.DTOs;

public sealed record TransactionDto(
    Guid Id,
    Guid UserId,
    TransactionType Type,
    decimal Amount,
    string Currency,
    TransactionCategory Category,
    string CategoryDisplayName,
    string Description,
    DateOnly TransactionDate,
    DateTimeOffset CreatedAt,
    bool IsAiParsed,
    decimal? AiConfidenceScore,
    string? OriginalMessage);
