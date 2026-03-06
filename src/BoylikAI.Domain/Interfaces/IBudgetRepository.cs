using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;

namespace BoylikAI.Domain.Interfaces;

public interface IBudgetRepository
{
    Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Budget>> GetByUserAndMonthAsync(
        Guid userId, int year, int month, CancellationToken ct = default);
    Task<Budget?> GetByUserAndCategoryAndMonthAsync(
        Guid userId, TransactionCategory? category, int year, int month, CancellationToken ct = default);
    Task AddAsync(Budget budget, CancellationToken ct = default);
    Task UpdateAsync(Budget budget, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
