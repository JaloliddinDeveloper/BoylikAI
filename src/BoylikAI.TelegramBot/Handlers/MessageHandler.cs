using BoylikAI.Application.Analytics.Queries.GetMonthlyReport;
using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using BoylikAI.Application.Transactions.Commands.ParseAndCreate;
using BoylikAI.Application.Users.Commands.RegisterUser;
using BoylikAI.TelegramBot.Keyboards;
using BoylikAI.TelegramBot.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BoylikAI.TelegramBot.Handlers;

public sealed class MessageHandler
{
    private readonly ITelegramBotClient _bot;
    private readonly IMediator _mediator;
    private readonly IChatService _chatService;
    private readonly ILogger<MessageHandler> _logger;

    public MessageHandler(
        ITelegramBotClient bot,
        IMediator mediator,
        IChatService chatService,
        ILogger<MessageHandler> logger)
    {
        _bot = bot;
        _mediator = mediator;
        _chatService = chatService;
        _logger = logger;
    }

    public async Task HandleAsync(Message message, CancellationToken ct)
    {
        if (message.From is null || message.Text is null) return;

        var text = message.Text.Trim();
        var chatId = message.Chat.Id;

        // Register or get user
        var userResult = await _mediator.Send(new RegisterUserCommand(
            message.From.Id,
            message.From.Username,
            message.From.FirstName,
            message.From.LastName,
            message.From.LanguageCode ?? "uz"), ct);

        var user = userResult.User;

        // Route to command handler or NLP parser
        if (text.StartsWith('/') || IsMenuCommand(text))
        {
            await HandleCommandAsync(text, chatId, user.Id, user.LanguageCode, ct);
            return;
        }

        // Natural language transaction parsing
        await HandleNaturalLanguageAsync(text, chatId, user.Id, user.LanguageCode, ct);
    }

    private async Task HandleNaturalLanguageAsync(
        string text, long chatId, Guid userId, string lang, CancellationToken ct)
    {
        // Send typing indicator
        await _bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

        var result = await _mediator.Send(new ParseAndCreateTransactionCommand(
            userId, chatId, text, lang), ct);

        if (result.Success && result.Transaction is not null)
        {
            var tx = result.Transaction;
            var emoji = tx.Type == Domain.Enums.TransactionType.Income ? "💰" : "💸";
            var typeLabel = tx.Type == Domain.Enums.TransactionType.Income
                ? (lang == "uz" ? "Daromad" : "Income")
                : (lang == "uz" ? "Xarajat" : "Expense");

            var response = lang == "uz"
                ? $"""
                   {emoji} *{typeLabel} saqlandi!*

                   💵 Miqdor: `{tx.Amount:N0} {tx.Currency}`
                   🏷 Kategoriya: {tx.CategoryDisplayName}
                   📝 Tavsif: {tx.Description}
                   📅 Sana: {tx.TransactionDate:d MMMM}
                   """
                : $"""
                   {emoji} *{typeLabel} saved!*

                   💵 Amount: `{tx.Amount:N0} {tx.Currency}`
                   🏷 Category: {tx.CategoryDisplayName}
                   📝 Description: {tx.Description}
                   📅 Date: {tx.TransactionDate:d MMM}
                   """;

            await _bot.SendMessage(
                chatId,
                response,
                parseMode: ParseMode.Markdown,
                replyMarkup: InlineKeyboardBuilder.ConfirmTransaction(tx.Id.ToString()),
                cancellationToken: ct);
        }
        else if (result.IsAmbiguous && result.ClarificationQuestion is not null)
        {
            await _bot.SendMessage(chatId, result.ClarificationQuestion,
                parseMode: ParseMode.Markdown, cancellationToken: ct);
        }
        else
        {
            // Moliyaviy tranzaksiya emas — Claude bilan do'stona suhbat
            var reply = await _chatService.ChatAsync(text, lang, ct);
            await _bot.SendMessage(chatId, reply, cancellationToken: ct);
        }
    }

    private async Task HandleCommandAsync(
        string command, long chatId, Guid userId, string lang, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        switch (command.ToLowerInvariant().Split('@')[0])
        {
            case "/start":
            case "start":
                await SendWelcomeMessageAsync(chatId, lang, ct);
                break;

            case "/hisobot":
            case "/report":
            case "📊 hisobot":
                await SendMonthlyReportAsync(chatId, userId, now.Year, now.Month, lang, ct);
                break;

            case "/maslahat":
            case "/advice":
            case "💡 maslahat":
                await SendAdviceAsync(chatId, userId, now.Year, now.Month, lang, ct);
                break;

            case "/prognoz":
            case "/prediction":
            case "📈 prognoz":
                await SendPredictionAsync(chatId, userId, now.Year, now.Month, lang, ct);
                break;

            case "/yordam":
            case "/help":
            case "❓ yordam":
                await SendHelpAsync(chatId, lang, ct);
                break;

            case "/reset":
            case "/tozala":
            case "🔄 hisobni tiklash":
                await SendResetConfirmationAsync(chatId, lang, ct);
                break;

            case "/export":
            case "/eksport":
            case "📤 excel eksport":
                await SendExportPeriodSelectionAsync(chatId, lang, ct);
                break;

            default:
                var msg = lang == "uz"
                    ? "Bu buyruq mavjud emas. /yordam ni bosing."
                    : "Unknown command. Use /help.";
                await _bot.SendMessage(chatId, msg, cancellationToken: ct);
                break;
        }
    }

    private async Task SendWelcomeMessageAsync(long chatId, string lang, CancellationToken ct)
    {
        var message = lang == "uz"
            ? """
              👋 *BoylikAI ga xush kelibsiz!*

              Men sizning shaxsiy moliyaviy yordamchingizman.

              📌 *Qanday foydalanish:*
              Shunchaki oddiy so'z bilan yozing:
              • `"Avtobusga 2400 so'm berdim"`
              • `"Kafeda 35 ming ishlatdim"`
              • `"Oylik oldim 5 million"`

              Qolganini men qilaman! ✨

              📊 /hisobot — oylik hisobot
              💡 /maslahat — moliyaviy maslahat
              📈 /prognoz — oylik prognoz
              ❓ /yordam — yordam
              """
            : """
              👋 *Welcome to BoylikAI!*

              I'm your personal financial assistant.

              📌 *How to use:*
              Just write naturally:
              • `"Paid 2400 for bus"`
              • `"Spent 35000 at cafe"`
              • `"Got salary 5 million"`

              I'll handle the rest! ✨

              📊 /report — monthly report
              💡 /advice — financial advice
              📈 /prediction — spending forecast
              ❓ /help — help
              """;

        await _bot.SendMessage(
            chatId, message,
            parseMode: ParseMode.Markdown,
            replyMarkup: InlineKeyboardBuilder.QuickActions(),
            cancellationToken: ct);
    }

    private async Task SendMonthlyReportAsync(
        long chatId, Guid userId, int year, int month, string lang, CancellationToken ct)
    {
        await _bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

        var report = await _mediator.Send(new GetMonthlyReportQuery(userId, year, month), ct);

        var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");
        var breakdown = string.Join("\n",
            report.CategoryBreakdown
                .OrderByDescending(c => c.Amount)
                .Select(c => $"  {GetCategoryEmoji(c.Category)} {c.CategoryDisplayName}: `{c.Amount:N0}` ({c.Percentage:F1}%)"));

        var message = lang == "uz"
            ? $"""
               📊 *{monthName} hisoboti*

               💰 Daromad: `{report.TotalIncome:N0} {report.Currency}`
               💸 Xarajat: `{report.TotalExpenses:N0} {report.Currency}`
               📈 Balans: `{report.NetBalance:N0} {report.Currency}`

               *Kategoriyalar bo'yicha:*
               {breakdown}
               """
            : $"""
               📊 *{monthName} Report*

               💰 Income: `{report.TotalIncome:N0} {report.Currency}`
               💸 Expenses: `{report.TotalExpenses:N0} {report.Currency}`
               📈 Balance: `{report.NetBalance:N0} {report.Currency}`

               *By category:*
               {breakdown}
               """;

        await _bot.SendMessage(chatId, message, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendAdviceAsync(
        long chatId, Guid userId, int year, int month, string lang, CancellationToken ct)
    {
        await _bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

        var advice = await _mediator.Send(new GetFinancialAdviceQuery(userId, year, month, lang), ct);

        var scoreEmoji = advice.HealthScore switch
        {
            Application.DTOs.HealthScore.Excellent => "🌟",
            Application.DTOs.HealthScore.Good => "✅",
            Application.DTOs.HealthScore.Fair => "⚠️",
            Application.DTOs.HealthScore.Poor => "🔴",
            _ => "🚨"
        };

        var tips = string.Join("\n", advice.ActionItems.Select((t, i) => $"{i + 1}. {t}"));

        var message = $"""
            {scoreEmoji} *Moliyaviy maslahat*

            {advice.Summary}

            *Tavsiyalar:*
            {tips}
            """;

        await _bot.SendMessage(chatId, message, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendPredictionAsync(
        long chatId, Guid userId, int year, int month, string lang, CancellationToken ct)
    {
        await _bot.SendChatAction(chatId, ChatAction.Typing, cancellationToken: ct);

        var pred = await _mediator.Send(new GetSpendingPredictionQuery(userId, year, month), ct);

        var dayPct = (int)Math.Round((double)pred.DaysElapsed / pred.DaysInMonth * 100);

        var trajectoryEmoji = pred.Trajectory switch
        {
            SpendingTrend.Improving => "↘️",
            SpendingTrend.Worsening => "↗️",
            _                       => "→"
        };
        var trajectoryLabel = lang == "uz"
            ? pred.Trajectory switch
            {
                SpendingTrend.Improving => "Kamayib bormoqda ✅",
                SpendingTrend.Worsening => "Ortib bormoqda ⚠️",
                _                       => "Barqaror"
            }
            : pred.Trajectory switch
            {
                SpendingTrend.Improving => "Decreasing ✅",
                SpendingTrend.Worsening => "Increasing ⚠️",
                _                       => "Stable"
            };

        var paceEmoji    = pred.SpendingPacePercent > 120 ? "🔴" : pred.SpendingPacePercent > 100 ? "🟡" : "🟢";
        var savingsEmoji = pred.ProjectedSavings >= 0 ? "💚" : "🔴";
        var confEmoji    = pred.Confidence switch
        {
            PredictionConfidence.High   => "🎯",
            PredictionConfidence.Medium => "📊",
            _                           => "📉"
        };

        // Top 4 kategoriya
        var catLines = string.Join("\n", pred.CategoryPredictions.Take(4).Select(c =>
        {
            var tEmoji = c.Trend switch
            {
                SpendingTrend.Worsening => "↗️",
                SpendingTrend.Improving => "↘️",
                _                       => "  →"
            };
            return $"  {GetCategoryEmoji(c.Category)} {c.CategoryDisplayName}: `{c.CurrentSpending:N0}` → `{c.PredictedTotal:N0}` {tEmoji}";
        }));

        var vsLastMonthLine = pred.VsLastMonthPercent.HasValue
            ? (lang == "uz"
                ? $"\n📋 *O'tgan oyga nisbatan:* {(pred.VsLastMonthPercent >= 0 ? "+" : "")}{pred.VsLastMonthPercent:F1}%"
                : $"\n📋 *vs Last Month:* {(pred.VsLastMonthPercent >= 0 ? "+" : "")}{pred.VsLastMonthPercent:F1}%")
            : "";

        var paceLabel = lang == "uz"
            ? pred.SpendingPacePercent switch
            {
                > 120 => "(ortiq sarflanmoqda)",
                > 100 => "(biroz ko'p)",
                > 80  => "(yaxshi)",
                _     => "(tejamkor) ✅"
            }
            : pred.SpendingPacePercent switch
            {
                > 120 => "(overspending)",
                > 100 => "(slightly over)",
                > 80  => "(on track)",
                _     => "(frugal) ✅"
            };

        var dailyBudgetLine = pred.DailyBudgetRemaining > 0
            ? (lang == "uz"
                ? $"\n💳 *Qolgan kunlarga limit:* `{pred.DailyBudgetRemaining:N0}` so'm/kun"
                : $"\n💳 *Daily budget left:* `{pred.DailyBudgetRemaining:N0}` UZS/day")
            : "";

        string message;
        if (lang == "uz")
        {
            message = $"""
                📈 *Oylik Xarajat Prognozi*
                ━━━━━━━━━━━━━━━━━━━━
                📅 *Davr:* {pred.DaysElapsed}/{pred.DaysInMonth} kun ({dayPct}% o'tdi)
                💸 *Hozirgi xarajat:* `{pred.CurrentSpending:N0}` so'm
                💡 *Kunlik o'rtacha (EWMA):* `{pred.AverageDailySpending:N0}` so'm
                💰 *Daromad:* `{pred.MonthlyIncome:N0}` so'm

                🔮 *Prognoz (3 stsenariy):*
                ├ 🎯 Asosiy:      `{pred.PredictedMonthEndSpending:N0}` so'm
                ├ 📉 Optimistik:  `{pred.PredictedLow:N0}` so'm
                └ 📈 Pessimistik: `{pred.PredictedHigh:N0}` so'm

                {savingsEmoji} *Kutilayotgan tejamkorlik:* `{pred.ProjectedSavings:N0}` so'm ({pred.ProjectedSavingsRate:F1}%){dailyBudgetLine}

                {paceEmoji} *Sarflash tempi:* {pred.SpendingPacePercent:F0}% {paceLabel}
                {trajectoryEmoji} *Trend:* {trajectoryLabel}{vsLastMonthLine}

                {confEmoji} *Ishonch:* {pred.ConfidencePercent}% | {pred.DaysElapsed} kunlik ma'lumot asosida
                """;
        }
        else
        {
            message = $"""
                📈 *Monthly Spending Forecast*
                ━━━━━━━━━━━━━━━━━━━━
                📅 *Progress:* {pred.DaysElapsed}/{pred.DaysInMonth} days ({dayPct}% elapsed)
                💸 *Current spending:* `{pred.CurrentSpending:N0}` UZS
                💡 *Daily average (EWMA):* `{pred.AverageDailySpending:N0}` UZS
                💰 *Income:* `{pred.MonthlyIncome:N0}` UZS

                🔮 *Forecast (3 scenarios):*
                ├ 🎯 Base:        `{pred.PredictedMonthEndSpending:N0}` UZS
                ├ 📉 Optimistic:  `{pred.PredictedLow:N0}` UZS
                └ 📈 Pessimistic: `{pred.PredictedHigh:N0}` UZS

                {savingsEmoji} *Projected savings:* `{pred.ProjectedSavings:N0}` UZS ({pred.ProjectedSavingsRate:F1}%){dailyBudgetLine}

                {paceEmoji} *Spending pace:* {pred.SpendingPacePercent:F0}% {paceLabel}
                {trajectoryEmoji} *Trend:* {trajectoryLabel}{vsLastMonthLine}

                {confEmoji} *Confidence:* {pred.ConfidencePercent}% | based on {pred.DaysElapsed} days
                """;
        }

        if (!string.IsNullOrEmpty(catLines))
        {
            var catHeader = lang == "uz" ? "\n\n📂 *Kategoriyalar bo'yicha prognoz:*" : "\n\n📂 *Category forecast:*";
            message += catHeader + "\n" + catLines;
        }

        if (!string.IsNullOrEmpty(pred.Warning))
            message += $"\n\n⚠️ _{pred.Warning}_";

        await _bot.SendMessage(chatId, message, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private async Task SendExportPeriodSelectionAsync(long chatId, string lang, CancellationToken ct)
    {
        var message = lang == "uz"
            ? "📊 *Excel eksport*\n\nQaysi davr uchun hisobot olmoqchisiz?"
            : "📊 *Excel Export*\n\nSelect the period for your report:";

        await _bot.SendMessage(
            chatId, message,
            parseMode: ParseMode.Markdown,
            replyMarkup: InlineKeyboardBuilder.ExportPeriodSelection(),
            cancellationToken: ct);
    }

    private async Task SendResetConfirmationAsync(long chatId, string lang, CancellationToken ct)
    {
        var message = lang == "uz"
            ? "⚠️ *Diqqat!*\n\nBarcha tranzaksiyalaringiz o'chirilib, hisob noldan boshlanadi.\n\nDavom etishni xohlaysizmi?"
            : "⚠️ *Warning!*\n\nAll your transactions will be deleted and your account will start fresh.\n\nAre you sure?";

        await _bot.SendMessage(
            chatId, message,
            parseMode: ParseMode.Markdown,
            replyMarkup: InlineKeyboardBuilder.ResetConfirmation(),
            cancellationToken: ct);
    }

    private async Task SendHelpAsync(long chatId, string lang, CancellationToken ct)
    {
        var message = lang == "uz"
            ? """
              ❓ *Yordam*

              *Tranzaksiyalar:*
              Oddiy so'z bilan yozing:
              • `"Avtobusga 2400 so'm berdim"` — Transport xarajati
              • `"Kafeda 35 ming ishlatdim"` — Ovqat xarajati
              • `"Oylik oldim 5 million"` — Daromad
              • `"Do'kondan 12 ming ga non oldim"` — Xarid

              *Buyruqlar:*
              /hisobot — Oylik hisobot
              /maslahat — Moliyaviy maslahat
              /prognoz — Oylik xarajat prognozi
              /yordam — Yordam
              """
            : """
              ❓ *Help*

              *Transactions:*
              Write naturally:
              • `"Paid 2400 for bus"` — Transport expense
              • `"Spent 35000 at cafe"` — Food expense
              • `"Got salary 5 million"` — Income
              • `"Bought bread for 12000"` — Shopping

              *Commands:*
              /report — Monthly report
              /advice — Financial advice
              /prediction — Monthly forecast
              /help — Help
              """;

        await _bot.SendMessage(chatId, message, parseMode: ParseMode.Markdown, cancellationToken: ct);
    }

    private static bool IsMenuCommand(string text) =>
        text is "📊 Hisobot" or "📊 hisobot" or "💡 Maslahat" or "💡 maslahat"
            or "📈 Prognoz" or "📈 prognoz" or "❓ Yordam" or "❓ yordam"
            or "🔄 Hisobni tiklash" or "🔄 hisobni tiklash"
            or "📤 Excel eksport" or "📤 excel eksport";

    private static string GetCategoryEmoji(Domain.Enums.TransactionCategory category) => category switch
    {
        Domain.Enums.TransactionCategory.Transport     => "🚌",
        Domain.Enums.TransactionCategory.Food          => "🍽",
        Domain.Enums.TransactionCategory.Shopping      => "🛒",
        Domain.Enums.TransactionCategory.Bills         => "🏦",
        Domain.Enums.TransactionCategory.Entertainment => "🎬",
        Domain.Enums.TransactionCategory.Health        => "💊",
        Domain.Enums.TransactionCategory.Education     => "📚",
        Domain.Enums.TransactionCategory.Housing       => "🏠",
        Domain.Enums.TransactionCategory.Savings       => "💎",
        Domain.Enums.TransactionCategory.Salary        => "💼",
        Domain.Enums.TransactionCategory.Freelance     => "💻",
        _ => "📌"
    };
}
