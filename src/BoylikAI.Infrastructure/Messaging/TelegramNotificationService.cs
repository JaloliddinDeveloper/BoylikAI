using BoylikAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace BoylikAI.Infrastructure.Messaging;

/// <summary>
/// Infrastructure-layer INotificationService implementation using the Telegram Bot API.
/// Used by the API project (DailyReportJob, budget alerts) without a circular dependency
/// on the TelegramBot project.
/// </summary>
public sealed class TelegramNotificationService : INotificationService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramNotificationService> _logger;

    public TelegramNotificationService(
        ITelegramBotClient bot,
        ILogger<TelegramNotificationService> logger)
    {
        _bot = bot;
        _logger = logger;
    }

    public async Task SendTextAsync(long telegramId, string message, CancellationToken ct = default)
    {
        try
        {
            await _bot.SendMessage(
                chatId: telegramId,
                text: message,
                parseMode: ParseMode.MarkdownV2,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send Telegram message to chat {TelegramId}", telegramId);
        }
    }

    public async Task SendBudgetAlertAsync(
        long telegramId, string categoryName, decimal usagePercent, CancellationToken ct = default)
    {
        var emoji = usagePercent >= 100 ? "🚨" : "⚠️";
        var text = usagePercent >= 100
            ? $"{emoji} *Byudjet chegarasi oshdi\\!*\n{EscapeMd(categoryName)} uchun byudjet {usagePercent:F0}% dan oshdi\\."
            : $"{emoji} *Byudjet ogohlantirishı*\n{EscapeMd(categoryName)} uchun byudjetning {usagePercent:F0}% sarflandi\\.";

        await SendTextAsync(telegramId, text, ct);
    }

    public async Task SendDailyReminderAsync(long telegramId, CancellationToken ct = default)
    {
        const string text = "📝 Bugungi xarajatlaringizni yozdingizmi\\?\n\nXarajat yoki daromadingizni oddiy so'z bilan yozib qoldiring\\.";
        await SendTextAsync(telegramId, text, ct);
    }

    // MarkdownV2 requires escaping these characters outside code/pre blocks
    private static string EscapeMd(string text) =>
        text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[")
            .Replace("]", "\\]").Replace("(", "\\(").Replace(")", "\\)")
            .Replace("~", "\\~").Replace("`", "\\`").Replace(">", "\\>")
            .Replace("#", "\\#").Replace("+", "\\+").Replace("-", "\\-")
            .Replace("=", "\\=").Replace("|", "\\|").Replace("{", "\\{")
            .Replace("}", "\\}").Replace(".", "\\.").Replace("!", "\\!");
}
