using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Interfaces;
using MediatR;

namespace BoylikAI.Application.Transactions.Queries.GetTransactions;

public sealed class GetTransactionsQueryHandler
    : IRequestHandler<GetTransactionsQuery, PagedResult<TransactionDto>>
{
    private readonly ITransactionRepository _transactionRepo;

    public GetTransactionsQueryHandler(ITransactionRepository transactionRepo)
    {
        _transactionRepo = transactionRepo;
    }

    public async Task<PagedResult<TransactionDto>> Handle(
        GetTransactionsQuery request,
        CancellationToken cancellationToken)
    {
        var from = request.From ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var to = request.To ?? DateOnly.FromDateTime(DateTime.UtcNow);

        var all = await _transactionRepo.GetByUserIdAndDateRangeAsync(
            request.UserId, from, to, cancellationToken);

        var filtered = all.AsEnumerable();
        if (request.Type.HasValue)
            filtered = filtered.Where(t => t.Type == request.Type.Value);
        if (request.Category.HasValue)
            filtered = filtered.Where(t => t.Category == request.Category.Value);

        var ordered = filtered.OrderByDescending(t => t.TransactionDate)
                               .ThenByDescending(t => t.CreatedAt)
                               .ToList();

        var totalCount = ordered.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);
        var items = ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(t => new TransactionDto(
                t.Id, t.UserId, t.Type,
                t.Amount.Amount, t.Amount.Currency,
                t.Category, t.Category.ToString(),
                t.Description, t.TransactionDate,
                t.CreatedAt, t.IsAiParsed,
                t.AiConfidenceScore, t.OriginalMessage))
            .ToList();

        return new PagedResult<TransactionDto>(items, totalCount, request.Page, request.PageSize, totalPages);
    }
}
