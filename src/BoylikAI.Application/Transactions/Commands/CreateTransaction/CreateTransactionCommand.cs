using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Enums;
using MediatR;

namespace BoylikAI.Application.Transactions.Commands.CreateTransaction;

public sealed record CreateTransactionCommand(
    Guid UserId,
    TransactionType Type,
    decimal Amount,
    string Currency,
    TransactionCategory Category,
    string Description,
    DateOnly TransactionDate) : IRequest<TransactionDto>;
