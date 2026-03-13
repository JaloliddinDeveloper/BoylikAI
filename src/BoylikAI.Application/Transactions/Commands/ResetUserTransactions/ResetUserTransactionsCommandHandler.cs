using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BoylikAI.Application.Transactions.Commands.ResetUserTransactions;

public sealed class ResetUserTransactionsCommandHandler
    : IRequestHandler<ResetUserTransactionsCommand, ResetUserTransactionsResult>
{
    private readonly ITransactionRepository _txRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<ResetUserTransactionsCommandHandler> _logger;

    public ResetUserTransactionsCommandHandler(
        ITransactionRepository txRepo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<ResetUserTransactionsCommandHandler> logger)
    {
        _txRepo = txRepo;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ResetUserTransactionsResult> Handle(
        ResetUserTransactionsCommand request,
        CancellationToken cancellationToken)
    {
        var count = await _txRepo.SoftDeleteAllByUserIdAsync(request.UserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveByPrefixAsync($"analytics:{request.UserId}", cancellationToken);

        _logger.LogInformation("User {UserId} reset their history: {Count} transactions deleted",
            request.UserId, count);

        return new ResetUserTransactionsResult(true, count);
    }
}
