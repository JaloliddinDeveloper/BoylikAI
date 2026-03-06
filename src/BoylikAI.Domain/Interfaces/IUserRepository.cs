using BoylikAI.Domain.Entities;

namespace BoylikAI.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> GetByTelegramIdAsync(long telegramId, CancellationToken ct = default);
    Task<bool> ExistsByTelegramIdAsync(long telegramId, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
    Task<IReadOnlyList<User>> GetActiveUsersAsync(CancellationToken ct = default);
}
