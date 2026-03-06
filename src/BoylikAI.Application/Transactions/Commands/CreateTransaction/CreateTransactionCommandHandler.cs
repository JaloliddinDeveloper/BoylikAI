using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Interfaces;
using MediatR;

namespace BoylikAI.Application.Transactions.Commands.CreateTransaction;

public sealed class CreateTransactionCommandHandler
    : IRequestHandler<CreateTransactionCommand, TransactionDto>
{
    private readonly ITransactionRepository _transactionRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public CreateTransactionCommandHandler(
        ITransactionRepository transactionRepo,
        IUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _transactionRepo = transactionRepo;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<TransactionDto> Handle(
        CreateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = Transaction.Create(
            request.UserId,
            request.Type,
            request.Amount,
            request.Currency,
            request.Category,
            request.Description,
            request.TransactionDate,
            isAiParsed: false);

        await _transactionRepo.AddAsync(transaction, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveByPrefixAsync($"analytics:{request.UserId}", cancellationToken);

        return new TransactionDto(
            transaction.Id,
            transaction.UserId,
            transaction.Type,
            transaction.Amount.Amount,
            transaction.Amount.Currency,
            transaction.Category,
            transaction.Category.ToString(),
            transaction.Description,
            transaction.TransactionDate,
            transaction.CreatedAt,
            false, null, null);
    }
}
