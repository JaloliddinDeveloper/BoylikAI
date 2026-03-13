using MediatR;

namespace BoylikAI.Application.Transactions.Commands.ResetUserTransactions;

public sealed record ResetUserTransactionsCommand(Guid UserId) : IRequest<ResetUserTransactionsResult>;

public sealed record ResetUserTransactionsResult(bool Success, int DeletedCount);
