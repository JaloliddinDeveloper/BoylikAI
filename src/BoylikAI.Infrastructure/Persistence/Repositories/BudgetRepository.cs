using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BoylikAI.Infrastructure.Persistence.Repositories;

public sealed class BudgetRepository : IBudgetRepository
{
    private readonly ApplicationDbContext _ctx;

    public BudgetRepository(ApplicationDbContext ctx) => _ctx = ctx;

    public async Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _ctx.Budgets.FindAsync(new object[] { id }, ct);

    public async Task<IReadOnlyList<Budget>> GetByUserAndMonthAsync(
        Guid userId, int year, int month, CancellationToken ct = default) =>
        await _ctx.Budgets
            .Where(b => b.UserId == userId && b.Year == year && b.Month == month)
            .ToListAsync(ct);

    public async Task<Budget?> GetByUserAndCategoryAndMonthAsync(
        Guid userId, TransactionCategory? category, int year, int month,
        CancellationToken ct = default) =>
        await _ctx.Budgets
            .FirstOrDefaultAsync(b =>
                b.UserId == userId
                && b.Year == year
                && b.Month == month
                && b.Category == category, ct);

    public async Task AddAsync(Budget budget, CancellationToken ct = default) =>
        await _ctx.Budgets.AddAsync(budget, ct);

    public Task UpdateAsync(Budget budget, CancellationToken ct = default)
    {
        _ctx.Budgets.Update(budget);
        return Task.CompletedTask;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var budget = await _ctx.Budgets.FindAsync(new object[] { id }, ct);
        if (budget is not null)
            _ctx.Budgets.Remove(budget);
    }
}
