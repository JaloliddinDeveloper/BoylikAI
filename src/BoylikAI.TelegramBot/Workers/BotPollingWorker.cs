using BoylikAI.TelegramBot.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BoylikAI.TelegramBot.Workers;

/// <summary>
/// Long-polling worker for development/single-server deployments.
/// In production with multiple replicas, use Webhook mode (WebhookController in BoylikAI.API).
/// </summary>
public sealed class BotPollingWorker : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BotPollingWorker> _logger;

    public BotPollingWorker(
        ITelegramBotClient bot,
        IServiceProvider serviceProvider,
        ILogger<BotPollingWorker> logger)
    {
        _bot = bot;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram bot long-polling started");

        var receiverOptions = new ReceiverOptions
        {
            // Voice is part of UpdateType.Message — no extra type needed
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery]
        };

        await _bot.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient bot,
        Update update,
        CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();

        try
        {
            if (update.Message is not null)
            {
                var handler = scope.ServiceProvider.GetRequiredService<MessageHandler>();
                await handler.HandleAsync(update.Message, ct);
            }
            else if (update.CallbackQuery is not null)
            {
                var handler = scope.ServiceProvider.GetRequiredService<CallbackQueryHandler>();
                await handler.HandleAsync(update.CallbackQuery, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing update {UpdateId}", update.Id);
        }
    }

    private Task HandleErrorAsync(
        ITelegramBotClient bot,
        Exception exception,
        CancellationToken ct)
    {
        _logger.LogError(exception, "Telegram polling error: {Message}", exception.Message);
        return Task.CompletedTask;
    }
}
