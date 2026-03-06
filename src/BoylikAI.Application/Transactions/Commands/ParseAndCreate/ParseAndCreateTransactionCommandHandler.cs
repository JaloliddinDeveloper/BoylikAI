using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BoylikAI.Application.Transactions.Commands.ParseAndCreate;

public sealed class ParseAndCreateTransactionCommandHandler
    : IRequestHandler<ParseAndCreateTransactionCommand, ParseAndCreateTransactionResult>
{
    private readonly ITransactionParser _parser;
    private readonly ITransactionRepository _transactionRepo;
    private readonly IUserRepository _userRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<ParseAndCreateTransactionCommandHandler> _logger;

    private const decimal MinConfidenceThreshold = 0.65m;

    public ParseAndCreateTransactionCommandHandler(
        ITransactionParser parser,
        ITransactionRepository transactionRepo,
        IUserRepository userRepo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<ParseAndCreateTransactionCommandHandler> logger)
    {
        _parser = parser;
        _transactionRepo = transactionRepo;
        _userRepo = userRepo;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ParseAndCreateTransactionResult> Handle(
        ParseAndCreateTransactionCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _userRepo.GetByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            return new ParseAndCreateTransactionResult(
                false, null, null, "User not found", false, null);
        }

        ParsedTransactionDto? parsed;
        try
        {
            parsed = await _parser.ParseAsync(
                request.RawMessage,
                request.UserId.ToString(),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse transaction message for user {UserId}: {Message}",
                request.UserId, request.RawMessage);
            return new ParseAndCreateTransactionResult(
                false, null, null,
                "Xabarni tushunib bo'lmadi. Iltimos, qayta urinib ko'ring.",
                false, null);
        }

        if (parsed is null)
        {
            return new ParseAndCreateTransactionResult(
                false, null, null,
                "Bu moliyaviy tranzaksiya emas. Iltimos, xarajat yoki daromad haqida yozing.",
                false, null);
        }

        if (parsed.ConfidenceScore < MinConfidenceThreshold)
        {
            var question = BuildClarificationQuestion(parsed, request.LanguageCode);
            return new ParseAndCreateTransactionResult(
                false, null, parsed, null, true, question);
        }

        var transaction = Transaction.Create(
            userId: request.UserId,
            type: parsed.Type,
            amount: parsed.Amount,
            currency: parsed.Currency,
            category: parsed.Category,
            description: parsed.Description,
            transactionDate: parsed.Date,
            originalMessage: request.RawMessage,
            isAiParsed: true,
            aiConfidenceScore: parsed.ConfidenceScore);

        await _transactionRepo.AddAsync(transaction, cancellationToken);
        user.UpdateLastActivity();
        // No explicit UpdateAsync needed — EF change tracking detects the mutation above
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Invalidate analytics cache for this user
        await _cache.RemoveByPrefixAsync($"analytics:{request.UserId}", cancellationToken);

        _logger.LogInformation(
            "Transaction created for user {UserId}: {Type} {Amount} {Currency} [{Category}]",
            request.UserId, parsed.Type, parsed.Amount, parsed.Currency, parsed.Category);

        var dto = MapToDto(transaction);
        return new ParseAndCreateTransactionResult(true, dto, parsed, null, false, null);
    }

    private static string BuildClarificationQuestion(ParsedTransactionDto parsed, string languageCode)
    {
        if (languageCode == "uz")
        {
            if (parsed.Amount == 0)
                return "Miqdorni aniqlab bering. Masalan: '35 000 so'm'";
            return $"Men tushundim: {parsed.Type} — {parsed.Amount:N0} {parsed.Currency} ({parsed.Category}). Bu to'g'rimi?";
        }
        return $"I understood: {parsed.Type} — {parsed.Amount:N0} {parsed.Currency} ({parsed.Category}). Is this correct?";
    }

    private static TransactionDto MapToDto(Transaction t) => new(
        t.Id,
        t.UserId,
        t.Type,
        t.Amount.Amount,
        t.Amount.Currency,
        t.Category,
        t.Category.ToString(),
        t.Description,
        t.TransactionDate,
        t.CreatedAt,
        t.IsAiParsed,
        t.AiConfidenceScore,
        t.OriginalMessage);
}
