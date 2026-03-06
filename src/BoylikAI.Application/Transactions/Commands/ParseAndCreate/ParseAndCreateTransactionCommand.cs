using BoylikAI.Application.DTOs;
using MediatR;

namespace BoylikAI.Application.Transactions.Commands.ParseAndCreate;

public sealed record ParseAndCreateTransactionCommand(
    Guid UserId,
    long TelegramId,
    string RawMessage,
    string LanguageCode = "uz") : IRequest<ParseAndCreateTransactionResult>;

public sealed record ParseAndCreateTransactionResult(
    bool Success,
    TransactionDto? Transaction,
    ParsedTransactionDto? ParsedData,
    string? ErrorMessage,
    bool IsAmbiguous,
    string? ClarificationQuestion);
