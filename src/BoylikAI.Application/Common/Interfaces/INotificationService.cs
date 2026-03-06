namespace BoylikAI.Application.Common.Interfaces;

public interface INotificationService
{
    Task SendTextAsync(long telegramId, string message, CancellationToken ct = default);
    Task SendBudgetAlertAsync(long telegramId, string categoryName, decimal usagePercent, CancellationToken ct = default);
    Task SendDailyReminderAsync(long telegramId, CancellationToken ct = default);
}
