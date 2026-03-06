using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BoylikAI.Infrastructure.Persistence.Repositories;

public sealed class TransactionRepository : ITransactionRepository
{
    private readonly ApplicationDbContext _ctx;

    public TransactionRepository(ApplicationDbContext ctx) => _ctx = ctx;

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _ctx.Transactions.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<Transaction>> GetByUserIdAsync(
        Guid userId, CancellationToken ct = default) =>
        await _ctx.Transactions
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Transaction>> GetByUserIdAndDateRangeAsync(
        Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await _ctx.Transactions
            .Where(t => t.UserId == userId
                && t.TransactionDate >= from
                && t.TransactionDate <= to)
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Transaction>> GetByUserIdAndMonthAsync(
        Guid userId, int year, int month, CancellationToken ct = default) =>
        await _ctx.Transactions
            .Where(t => t.UserId == userId
                && t.TransactionDate.Year == year
                && t.TransactionDate.Month == month)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync(ct);

    /// <summary>
    /// DB-level aggregation — avoids loading all transactions into memory.
    /// Uses the covering index ix_transactions_analytics_covering.
    /// </summary>
    public async Task<IReadOnlyList<CategorySummary>> GetMonthlyCategorySummaryAsync(
        Guid userId, int year, int month, TransactionType type, CancellationToken ct = default)
    {
        return await _ctx.Transactions
            .Where(t => t.UserId == userId
                && t.TransactionDate.Year == year
                && t.TransactionDate.Month == month
                && t.Type == type)
            .GroupBy(t => t.Category)
            .Select(g => new CategorySummary(g.Key, g.Sum(t => t.Amount.Amount), g.Count()))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Returns (income, expenses) totals in a single DB round-trip.
    /// </summary>
    public async Task<(decimal Income, decimal Expenses)> GetMonthlyTotalsAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        var totals = await _ctx.Transactions
            .Where(t => t.UserId == userId
                && t.TransactionDate.Year == year
                && t.TransactionDate.Month == month)
            .GroupBy(t => t.Type)
            .Select(g => new { Type = g.Key, Total = g.Sum(t => t.Amount.Amount) })
            .ToListAsync(ct);

        var income = totals.FirstOrDefault(t => t.Type == TransactionType.Income)?.Total ?? 0m;
        var expenses = totals.FirstOrDefault(t => t.Type == TransactionType.Expense)?.Total ?? 0m;
        return (income, expenses);
    }

    public async Task<decimal> GetTotalByUserAndCategoryAsync(
        Guid userId, TransactionCategory category, int year, int month, CancellationToken ct = default) =>
        await _ctx.Transactions
            .Where(t => t.UserId == userId
                && t.Category == category
                && t.TransactionDate.Year == year
                && t.TransactionDate.Month == month
                && t.Type == TransactionType.Expense)
            .SumAsync(t => t.Amount.Amount, ct);

    public async Task AddAsync(Transaction transaction, CancellationToken ct = default) =>
        await _ctx.Transactions.AddAsync(transaction, ct);

    public Task UpdateAsync(Transaction transaction, CancellationToken ct = default)
    {
        _ctx.Transactions.Update(transaction);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Soft delete — moliyaviy ma'lumotlar hech qachon fizikaviy o'chirilmaydi.
    /// Domain event raises BudgetExceededEvent chain if needed.
    /// </summary>
    public async Task SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        // IgnoreQueryFilters() — already-deleted transactions can be fetched for idempotency
        var tx = await _ctx.Transactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, ct);

        if (tx is null || tx.IsDeleted) return;
        tx.SoftDelete();
        // EF change tracking handles the UPDATE — no explicit .Update() call needed
    }
}
