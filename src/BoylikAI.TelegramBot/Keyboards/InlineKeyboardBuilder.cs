using Telegram.Bot.Types.ReplyMarkups;

namespace BoylikAI.TelegramBot.Keyboards;

public static class InlineKeyboardBuilder
{
    public static InlineKeyboardMarkup ConfirmTransaction(string transactionId) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ To'g'ri", $"confirm:{transactionId}"),
                InlineKeyboardButton.WithCallbackData("✏️ Tahrirlash", $"edit:{transactionId}"),
                InlineKeyboardButton.WithCallbackData("❌ O'chirish", $"delete:{transactionId}")
            }
        });

    public static InlineKeyboardMarkup MainMenu() =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📊 Hisobot", "menu:report"),
                InlineKeyboardButton.WithCallbackData("💡 Maslahat", "menu:advice")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📈 Prognoz", "menu:prediction"),
                InlineKeyboardButton.WithCallbackData("⚙️ Sozlamalar", "menu:settings")
            }
        });

    public static InlineKeyboardMarkup ResetConfirmation() =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Ha, o'chir", "reset_confirm:yes"),
                InlineKeyboardButton.WithCallbackData("❌ Bekor", "cancel:reset")
            }
        });

    public static ReplyKeyboardMarkup QuickActions() =>
        new(new[]
        {
            new KeyboardButton[] { "📊 Hisobot", "💡 Maslahat" },
            new KeyboardButton[] { "📈 Prognoz", "❓ Yordam" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = false
        };
}
