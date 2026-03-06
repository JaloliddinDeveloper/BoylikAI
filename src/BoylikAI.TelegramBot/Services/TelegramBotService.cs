using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BoylikAI.TelegramBot.Services;

/// <summary>
/// Central service that routes incoming Telegram updates to the correct handler.
/// Implements the Chain of Responsibility pattern for update processing.
/// </summary>
public sealed class TelegramBotService : INotificationService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(ITelegramBotClient bot, ILogger<TelegramBotService> logger)
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
                parseMode: ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send message to {TelegramId}", telegramId);
        }
    }

    public async Task SendBudgetAlertAsync(
        long telegramId, string categoryName, decimal usagePercent, CancellationToken ct = default)
    {
        var emoji = usagePercent >= 100 ? "🚨" : "⚠️";
        var message = usagePercent >= 100
            ? $"{emoji} *Byudjet chegarasi oshdi!*\n{categoryName} uchun byudjetingiz {usagePercent:F0}% dan oshdi."
            : $"{emoji} *Byudjet ogohlantirishı*\n{categoryName} uchun byudjetingizning {usagePercent:F0}% ini ishlatdingiz.";

        await SendTextAsync(telegramId, message, ct);
    }

    public async Task SendDailyReminderAsync(long telegramId, CancellationToken ct = default)
    {
        var message = "📝 Bugungi xarajatlaringizni yozdingizmi?\n\nXarajat yoki daromadingizni oddiy so'z bilan yozib qoldiring.";
        await SendTextAsync(telegramId, message, ct);
    }
}
