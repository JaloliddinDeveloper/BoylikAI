using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.Transactions.Commands.ParseAndCreate;
using BoylikAI.Application.Users.Commands.RegisterUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoylikAI.API.Controllers;

/// <summary>
/// Receives Telegram webhook updates.
/// Security: secret is in the X-Telegram-Bot-Api-Secret-Token header (not the URL path).
/// Idempotency: duplicate update_id values are ignored via a short-lived cache.
/// </summary>
[ApiController]
[Route("api/webhook/telegram")]
[AllowAnonymous] // Uses its own secret-token validation, not JWT
public sealed class WebhookController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly INotificationService _notificationService;
    private readonly ILogger<WebhookController> _logger;
    private readonly ICacheService _cache;
    private readonly string? _webhookSecret;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WebhookController(
        IMediator mediator,
        INotificationService notificationService,
        ICacheService cache,
        ILogger<WebhookController> logger,
        IConfiguration configuration)
    {
        _mediator = mediator;
        _notificationService = notificationService;
        _cache = cache;
        _logger = logger;
        _webhookSecret = configuration["Telegram:WebhookSecret"];
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> HandleUpdate(
        [FromBody] TelegramUpdate update,
        CancellationToken ct = default)
    {
        // 1. Validate secret token from header (not URL) — timing-safe comparison
        if (!ValidateSecret(Request.Headers["X-Telegram-Bot-Api-Secret-Token"]))
        {
            _logger.LogWarning("Webhook request with invalid secret from {IP}",
                HttpContext.Connection.RemoteIpAddress);
            // Always return 200 to Telegram to prevent retry storms
            return Ok();
        }

        // 2. Idempotency — Telegram may deliver the same update more than once
        var idempotencyKey = $"webhook:update:{update.UpdateId}";
        if (await _cache.ExistsAsync(idempotencyKey, ct))
        {
            _logger.LogDebug("Duplicate update_id {UpdateId} ignored", update.UpdateId);
            return Ok();
        }
        // Mark as processed for 24 hours (Telegram retries for up to 1 hour)
        await _cache.SetAsync(idempotencyKey, new ProcessedMarker(), TimeSpan.FromHours(24), ct);

        // 3. Route to the appropriate handler
        try
        {
            if (update.Message?.Text is not null)
                await HandleMessageAsync(update.Message, ct);
            else if (update.CallbackQuery is not null)
                await HandleCallbackQueryAsync(update.CallbackQuery, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Telegram update {UpdateId}", update.UpdateId);
            // Still return 200 — returning 5xx causes Telegram to retry endlessly
        }

        return Ok();
    }

    private async Task HandleMessageAsync(TelegramMessage msg, CancellationToken ct)
    {
        if (msg.From is null) return;

        var telegramId = msg.From.Id;
        var chatId = msg.Chat?.Id ?? telegramId;

        _logger.LogInformation("Telegram message from {TelegramId}: {Text}",
            telegramId, msg.Text?[..Math.Min(50, msg.Text.Length)]);

        // Register or retrieve user (upsert)
        var userResult = await _mediator.Send(new RegisterUserCommand(
            telegramId,
            msg.From.Username,
            msg.From.FirstName,
            msg.From.LastName,
            msg.From.LanguageCode ?? "uz"), ct);

        var userId = userResult.User.Id;
        var langCode = userResult.User.LanguageCode;

        // Handle bot commands
        if (msg.Text!.StartsWith('/'))
        {
            await HandleCommandAsync(chatId, msg.Text, userId, langCode, ct);
            return;
        }

        // Parse and create transaction from natural language
        ParseAndCreateTransactionResult parseResult;
        try
        {
            parseResult = await _mediator.Send(new ParseAndCreateTransactionCommand(
                userId, telegramId, msg.Text, langCode), ct);
        }
        catch (InvalidOperationException ex) when (ex.Message == "rate_limited")
        {
            var rateLimitMsg = langCode == "uz"
                ? "⚠️ Juda ko'p so'rov yuborildi. Iltimos, bir daqiqa kuting."
                : "⚠️ Too many requests. Please wait a minute.";
            await _notificationService.SendTextAsync(chatId, rateLimitMsg, ct);
            return;
        }

        // Send response based on parse result
        if (parseResult.IsAmbiguous && parseResult.ClarificationQuestion is not null)
        {
            await _notificationService.SendTextAsync(chatId, parseResult.ClarificationQuestion, ct);
        }
        else if (parseResult.Success && parseResult.Transaction is not null)
        {
            var tx = parseResult.Transaction;
            var emoji = tx.Type.ToString() == "Income" ? "💰" : "💸";
            var reply = langCode == "uz"
                ? $"{emoji} Saqlandi: {tx.Amount:N0} {tx.Currency} — {tx.CategoryDisplayName}"
                : $"{emoji} Saved: {tx.Amount:N0} {tx.Currency} — {tx.CategoryDisplayName}";
            await _notificationService.SendTextAsync(chatId, reply, ct);
        }
        else if (!string.IsNullOrEmpty(parseResult.ErrorMessage))
        {
            await _notificationService.SendTextAsync(chatId, parseResult.ErrorMessage, ct);
        }
    }

    private async Task HandleCommandAsync(
        long chatId, string text, Guid userId, string langCode, CancellationToken ct)
    {
        var command = text.Split(' ')[0].ToLowerInvariant();
        var now = DateTime.UtcNow;

        var reply = command switch
        {
            "/start" => langCode == "uz"
                ? "Assalomu alaykum! 💰 Men BoylikAI — shaxsiy moliyaviy yordamchingizman.\n\nXarajat yoki daromadingizni oddiy so'z bilan yozing.\n\nMasalan: *Kafe 35 ming*"
                : "Hello! 💰 I'm BoylikAI — your personal finance assistant.\n\nJust write your expense or income in plain text.\n\nExample: *Coffee 35000*",

            "/help" => langCode == "uz"
                ? "📌 *Qanday ishlatish:*\n\n• Xarajat: `Kafe 35 ming`\n• Daromad: `Oylik 5 million oldim`\n• Hisobot: /hisobot\n• Prognoz: /prognoz"
                : "📌 *How to use:*\n\n• Expense: `Coffee 35000`\n• Income: `Received salary 5mln`\n• Report: /report\n• Forecast: /forecast",

            "/hisobot" or "/report" => null, // Handled by separate query flow

            _ => langCode == "uz"
                ? "❓ Noma'lum buyruq. /help ni ko'ring."
                : "❓ Unknown command. See /help."
        };

        if (reply is not null)
            await _notificationService.SendTextAsync(chatId, reply, ct);
    }

    private async Task HandleCallbackQueryAsync(TelegramCallbackQuery query, CancellationToken ct)
    {
        if (query.Message is null || string.IsNullOrEmpty(query.Data)) return;

        _logger.LogDebug("Callback query from {UserId}: {Data}", query.From?.Id, query.Data);

        // Callback data format: "action:param" e.g. "confirm:txId", "delete:txId"
        var parts = query.Data.Split(':', 2);
        if (parts.Length != 2) return;

        var (action, param) = (parts[0], parts[1]);

        // Callback handling is intentionally minimal in the API webhook —
        // complex interactions live in the TelegramBot polling project.
        _logger.LogInformation("Callback action={Action} param={Param}", action, param);
    }

    private bool ValidateSecret(string? receivedSecret)
    {
        if (string.IsNullOrEmpty(_webhookSecret))
        {
            _logger.LogError("Telegram:WebhookSecret is not configured — all webhook requests will be rejected");
            return false;
        }

        if (string.IsNullOrEmpty(receivedSecret)) return false;

        // Timing-safe comparison prevents timing attacks
        var expected = Encoding.UTF8.GetBytes(_webhookSecret);
        var received = Encoding.UTF8.GetBytes(receivedSecret);
        return CryptographicOperations.FixedTimeEquals(expected, received);
    }

    private sealed record ProcessedMarker;
}

// ── Minimal Telegram update models ───────────────────────────────────────────

public sealed class TelegramUpdate
{
    [JsonPropertyName("update_id")] public long UpdateId { get; set; }
    [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
    [JsonPropertyName("callback_query")] public TelegramCallbackQuery? CallbackQuery { get; set; }
}

public sealed class TelegramMessage
{
    [JsonPropertyName("message_id")] public long MessageId { get; set; }
    [JsonPropertyName("from")] public TelegramUser? From { get; set; }
    [JsonPropertyName("chat")] public TelegramChat? Chat { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("date")] public long Date { get; set; }
}

public sealed class TelegramCallbackQuery
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("from")] public TelegramUser? From { get; set; }
    [JsonPropertyName("message")] public TelegramMessage? Message { get; set; }
    [JsonPropertyName("data")] public string? Data { get; set; }
}

public sealed class TelegramUser
{
    [JsonPropertyName("id")] public long Id { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("language_code")] public string? LanguageCode { get; set; }
}

public sealed class TelegramChat
{
    [JsonPropertyName("id")] public long Id { get; set; }
}
