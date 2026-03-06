using BoylikAI.Domain.Enums;

namespace BoylikAI.Application.DTOs;

public sealed record ParsedTransactionDto(
    TransactionType Type,
    decimal Amount,
    string Currency,
    TransactionCategory Category,
    string Description,
    DateOnly Date,
    decimal ConfidenceScore,
    string? OriginalMessage = null);
