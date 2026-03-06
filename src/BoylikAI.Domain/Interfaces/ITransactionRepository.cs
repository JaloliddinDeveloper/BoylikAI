using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;

namespace BoylikAI.Domain.Interfaces;

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> GetByUserIdAndDateRangeAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<IReadOnlyList<Transaction>> GetByUserIdAndMonthAsync(
        Guid userId, int year, int month, CancellationToken ct = default);

    // DB-level aggregation — in-memory grouping o'rniga
    Task<IReadOnlyList<CategorySummary>> GetMonthlyCategorySummaryAsync(
        Guid userId, int year, int month, TransactionType type, CancellationToken ct = default);
    Task<(decimal Income, decimal Expenses)> GetMonthlyTotalsAsync(
        Guid userId, int year, int month, CancellationToken ct = default);

    Task<decimal> GetTotalByUserAndCategoryAsync(
        Guid userId, TransactionCategory category, int year, int month, CancellationToken ct = default);

    Task AddAsync(Transaction transaction, CancellationToken ct = default);
    Task UpdateAsync(Transaction transaction, CancellationToken ct = default);

    /// <summary>Soft delete — moliyaviy ma'lumotlar hech qachon yo'q qilinmaydi.</summary>
    Task SoftDeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>DB-level aggregation natijasi. In-memory'dan tezroq.</summary>
public sealed record CategorySummary(
    TransactionCategory Category,
    decimal TotalAmount,
    int Count);
