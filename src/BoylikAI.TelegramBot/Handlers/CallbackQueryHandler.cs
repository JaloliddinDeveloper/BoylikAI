using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.Transactions.Commands.ResetUserTransactions;
using BoylikAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BoylikAI.TelegramBot.Handlers;

/// <summary>
/// Handles inline keyboard button presses (CallbackQuery updates).
/// Callback data format: "action:param" — e.g. "delete:&lt;txId&gt;", "confirm:&lt;txId&gt;".
/// </summary>
public sealed class CallbackQueryHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly ITransactionRepository _txRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRepository _userRepo;
    private readonly IMediator _mediator;
    private readonly IExcelExportService _excelExport;
    private readonly ILogger<CallbackQueryHandler> _logger;

    public CallbackQueryHandler(
        ITelegramBotClient bot,
        ITransactionRepository txRepo,
        IUnitOfWork unitOfWork,
        IUserRepository userRepo,
        IMediator mediator,
        IExcelExportService excelExport,
        ILogger<CallbackQueryHandler> logger)
    {
        _bot = bot;
        _txRepo = txRepo;
        _unitOfWork = unitOfWork;
        _userRepo = userRepo;
        _mediator = mediator;
        _excelExport = excelExport;
        _logger = logger;
    }

    public async Task HandleAsync(CallbackQuery query, CancellationToken ct = default)
    {
        if (query.Message is null || string.IsNullOrEmpty(query.Data)) return;

        var chatId = query.Message.Chat.Id;
        var telegramUserId = query.From.Id;

        var parts = query.Data.Split(':', 2);
        if (parts.Length != 2)
        {
            await AnswerCallbackAsync(query.Id, string.Empty, ct);
            return;
        }

        var (action, param) = (parts[0], parts[1]);

        var user = await _userRepo.GetByTelegramIdAsync(telegramUserId, ct);
        var lang = user?.LanguageCode ?? "uz";

        switch (action)
        {
            case "confirm":
                await HandleConfirmAsync(query.Id, chatId, query.Message.MessageId, lang, ct);
                break;

            case "edit":
                await HandleEditAsync(query.Id, chatId, query.Message.MessageId, param, lang, ct);
                break;

            case "delete":
            case "delete_tx":
                await HandleDeleteAsync(query.Id, chatId, query.Message.MessageId, param, lang, ct);
                break;

            case "export":
                await HandleExportAsync(query.Id, chatId, user?.Id ?? Guid.Empty, param, lang, ct);
                break;

            case "reset_confirm":
                await HandleResetConfirmAsync(query.Id, chatId, user?.Id ?? Guid.Empty, lang, ct);
                break;

            case "cancel":
                await AnswerCallbackAsync(query.Id, lang == "uz" ? "Bekor qilindi" : "Cancelled", ct);
                await _bot.EditMessageReplyMarkup(
                    chatId: chatId,
                    messageId: query.Message.MessageId,
                    replyMarkup: null,
                    cancellationToken: ct);
                break;

            default:
                _logger.LogWarning("Unknown callback action: {Action}", action);
                await AnswerCallbackAsync(query.Id, string.Empty, ct);
                break;
        }
    }

    private async Task HandleExportAsync(
        string callbackQueryId, long chatId, Guid userId, string periodStr, string lang, CancellationToken ct)
    {
        if (userId == Guid.Empty)
        {
            await AnswerCallbackAsync(callbackQueryId, "Xatolik", ct);
            return;
        }

        var period = periodStr switch
        {
            "weekly"  => ExportPeriod.Weekly,
            "monthly" => ExportPeriod.Monthly,
            _         => ExportPeriod.Daily
        };

        try
        {
            await AnswerCallbackAsync(callbackQueryId, lang == "uz" ? "Tayyorlanmoqda..." : "Preparing...", ct);
            await _bot.SendChatAction(chatId, ChatAction.UploadDocument, cancellationToken: ct);

            var (bytes, fileName) = await _excelExport.ExportAsync(userId, period, ct);

            using var stream = new MemoryStream(bytes);
            var caption = lang == "uz"
                ? $"📊 Excel hisobot tayyor! ({period switch { ExportPeriod.Weekly => "Haftalik", ExportPeriod.Monthly => "Oylik", _ => "Bugungi" }})"
                : $"📊 Excel report ready! ({period switch { ExportPeriod.Weekly => "Weekly", ExportPeriod.Monthly => "Monthly", _ => "Daily" }})";

            await _bot.SendDocument(
                chatId: chatId,
                document: InputFile.FromStream(stream, fileName),
                caption: caption,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel export failed for user {UserId}, period {Period}", userId, period);
            var errMsg = lang == "uz"
                ? "❌ Hisobot yaratishda xatolik yuz berdi."
                : "❌ Failed to generate the report.";
            await _bot.SendMessage(chatId, errMsg, cancellationToken: ct);
        }
    }

    // ── ✅ To'g'ri ────────────────────────────────────────────────────────
    private async Task HandleConfirmAsync(
        string callbackQueryId, long chatId, int messageId, string lang, CancellationToken ct)
    {
        var toast = lang == "uz" ? "✅ Saqlandi!" : "✅ Saved!";
        await AnswerCallbackAsync(callbackQueryId, toast, ct);

        // Inline klaviaturani olib tashlaymiz — tranzaksiya tasdiqlandi
        try
        {
            await _bot.EditMessageReplyMarkup(
                chatId: chatId,
                messageId: messageId,
                replyMarkup: null,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EditMessageReplyMarkup failed on confirm");
        }
    }

    // ── ✏️ Tahrirlash ─────────────────────────────────────────────────────
    private async Task HandleEditAsync(
        string callbackQueryId, long chatId, int messageId, string txIdStr, string lang, CancellationToken ct)
    {
        if (!Guid.TryParse(txIdStr, out var txId))
        {
            await AnswerCallbackAsync(callbackQueryId, "Invalid ID", ct);
            return;
        }

        try
        {
            var tx = await _txRepo.GetByIdAsync(txId, ct);
            if (tx is null)
            {
                await AnswerCallbackAsync(callbackQueryId, lang == "uz" ? "Topilmadi" : "Not found", ct);
                return;
            }

            // Eski tranzaksiyani o'chiramiz
            await _txRepo.SoftDeleteAsync(txId, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            // Inline klaviaturani olib tashlaymiz
            try
            {
                await _bot.EditMessageReplyMarkup(
                    chatId: chatId,
                    messageId: messageId,
                    replyMarkup: null,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EditMessageReplyMarkup failed on edit");
            }

            var typeLabel = tx.Type == Domain.Enums.TransactionType.Income
                ? (lang == "uz" ? "Daromad" : "Income")
                : (lang == "uz" ? "Xarajat" : "Expense");

            var hint = lang == "uz"
                ? $"""
                   ✏️ *Tahrirlash uchun qayta yuboring*

                   O'chirilgan yozuv:
                   └ {typeLabel}: `{tx.Amount.Amount:N0} {tx.Amount.Currency}` — {tx.Description}

                   To'g'ri ma'lumotni yuboring, men saqlab qo'yaman 👇
                   """
                : $"""
                   ✏️ *Re-enter to edit*

                   Deleted entry:
                   └ {typeLabel}: `{tx.Amount.Amount:N0} {tx.Amount.Currency}` — {tx.Description}

                   Send the corrected version and I'll save it 👇
                   """;

            await AnswerCallbackAsync(callbackQueryId, lang == "uz" ? "Tahrirlash uchun qayta yozing" : "Re-enter to edit", ct);
            await _bot.SendMessage(chatId, hint, parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start edit for transaction {TxId}", txId);
            await AnswerCallbackAsync(callbackQueryId, "❌ Xatolik", ct);
        }
    }

    // ── ❌ O'chirish ──────────────────────────────────────────────────────
    private async Task HandleDeleteAsync(
        string callbackQueryId, long chatId, int messageId, string txIdStr, string lang, CancellationToken ct)
    {
        if (!Guid.TryParse(txIdStr, out var txId))
        {
            await AnswerCallbackAsync(callbackQueryId, "Invalid ID", ct);
            return;
        }

        try
        {
            await _txRepo.SoftDeleteAsync(txId, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            // Inline klaviaturani olib tashlaymiz
            try
            {
                await _bot.EditMessageReplyMarkup(
                    chatId: chatId,
                    messageId: messageId,
                    replyMarkup: null,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EditMessageReplyMarkup failed on delete");
            }

            var successMsg = lang == "uz"
                ? "🗑 Tranzaksiya o'chirildi."
                : "🗑 Transaction deleted.";

            await AnswerCallbackAsync(callbackQueryId, successMsg, ct);
            await _bot.SendMessage(chatId, successMsg, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete transaction {TxId}", txId);
            var errorMsg = lang == "uz"
                ? "❌ O'chirishda xatolik yuz berdi."
                : "❌ Failed to delete transaction.";
            await AnswerCallbackAsync(callbackQueryId, errorMsg, ct);
        }
    }

    private async Task HandleResetConfirmAsync(
        string callbackQueryId, long chatId, Guid userId, string lang, CancellationToken ct)
    {
        if (userId == Guid.Empty)
        {
            await AnswerCallbackAsync(callbackQueryId, "Xatolik", ct);
            return;
        }

        try
        {
            var result = await _mediator.Send(new ResetUserTransactionsCommand(userId), ct);

            var msg = lang == "uz"
                ? $"✅ Barcha ma'lumotlar o'chirildi. ({result.DeletedCount} ta tranzaksiya)\n\nHisobingiz noldan boshlandi! 🚀"
                : $"✅ All data cleared. ({result.DeletedCount} transactions)\n\nFresh start! 🚀";

            await AnswerCallbackAsync(callbackQueryId, lang == "uz" ? "O'chirildi!" : "Cleared!", ct);
            await _bot.SendMessage(chatId, msg, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset transactions for user {UserId}", userId);
            await AnswerCallbackAsync(callbackQueryId, "❌ Xatolik", ct);
        }
    }

    private async Task AnswerCallbackAsync(string callbackQueryId, string text, CancellationToken ct)
    {
        try
        {
            await _bot.AnswerCallbackQuery(
                callbackQueryId: callbackQueryId,
                text: text,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to answer callback query {Id}", callbackQueryId);
        }
    }
}
