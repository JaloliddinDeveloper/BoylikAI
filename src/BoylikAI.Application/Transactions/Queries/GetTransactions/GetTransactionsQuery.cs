using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Enums;
using MediatR;

namespace BoylikAI.Application.Transactions.Queries.GetTransactions;

public sealed record GetTransactionsQuery(
    Guid UserId,
    DateOnly? From = null,
    DateOnly? To = null,
    TransactionType? Type = null,
    TransactionCategory? Category = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<TransactionDto>>;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);
