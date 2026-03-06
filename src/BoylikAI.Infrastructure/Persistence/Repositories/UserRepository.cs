using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BoylikAI.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _ctx;

    public UserRepository(ApplicationDbContext ctx) => _ctx = ctx;

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _ctx.Users.FindAsync(new object[] { id }, ct);

    public async Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default) =>
        await _ctx.Users.FirstOrDefaultAsync(u => u.TelegramId == telegramId, ct);

    public async Task<bool> ExistsByTelegramIdAsync(long telegramId, CancellationToken ct = default) =>
        await _ctx.Users.AnyAsync(u => u.TelegramId == telegramId, ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await _ctx.Users.AddAsync(user, ct);

    public Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _ctx.Users.Update(user);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<User>> GetActiveUsersAsync(CancellationToken ct = default) =>
        await _ctx.Users.Where(u => u.IsActive).ToListAsync(ct);
}
