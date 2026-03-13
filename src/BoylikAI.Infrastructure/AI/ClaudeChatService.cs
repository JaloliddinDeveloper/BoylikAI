using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using BoylikAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BoylikAI.Infrastructure.AI;

public sealed class ClaudeChatService : IChatService
{
    private readonly AnthropicClient _client;
    private readonly AnthropicOptions _options;
    private readonly ILogger<ClaudeChatService> _logger;

    public ClaudeChatService(
        AnthropicClient client,
        IOptions<AnthropicOptions> options,
        ILogger<ClaudeChatService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> ChatAsync(string message, string languageCode, CancellationToken ct = default)
    {
        try
        {
            var request = new MessageParameters
            {
                Model = _options.AdviceModel,
                MaxTokens = 300,
                Messages = new List<Message>
                {
                    new(RoleType.User, message)
                },
                SystemMessage = GetSystemPrompt(languageCode)
            };

            var response = await _client.Messages.GetClaudeMessageAsync(request, ct);
            var reply = response.Message.ToString()?.Trim() ?? string.Empty;

            return string.IsNullOrEmpty(reply)
                ? GetFallback(languageCode)
                : reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat service failed for message: {Message}", message);
            return GetFallback(languageCode);
        }
    }

    private static string GetSystemPrompt(string languageCode) => languageCode == "uz"
        ? """
          Siz BoylikAI — do'stona va mehribon shaxsiy moliyaviy yordamchisiz.
          Foydalanuvchi bilan o'zbek tilida suhbat qilasiz.

          QOIDALAR:
          - Har doim iliq, samimiy va hurmatli bo'ling
          - Qisqa va aniq javob bering (1-3 jumla)
          - Salomlashuvlarga issiq salomlashing
          - Savollarga foydali javob bering
          - Hech qachon foydalanuvchini rad etmang yoki sovuq muomila qilmang
          - Emoji ishlating — bu suhbatni jonliroq qiladi

          MUHIM — Agar xabar moliyaviy tranzaksiyaga o'xshasa (pul miqdori, daromad, xarajat):
          - Ko'p savol bermang!
          - Foydalanuvchiga aniqroq yozishni taklif qiling, masalan:
            "Daromadni saqlash uchun: '237,000 sum daromad' deb yozing 💡"
          - 1 ta qisqa jumla bilan yetarli
          """
        : """
          You are BoylikAI — a friendly and warm personal financial assistant.
          Chat with the user naturally in English.

          RULES:
          - Always be warm, genuine, and respectful
          - Keep responses short and clear (1-3 sentences)
          - Greet warmly when greeted
          - Answer questions helpfully
          - Never be cold or dismissive
          - Use emojis to make conversation lively

          IMPORTANT — If the message looks like a financial transaction (amount, income, expense):
          - Don't ask multiple questions!
          - Guide them to write it clearly, e.g.:
            "To save income, write: '237,000 sum income' 💡"
          - One short sentence is enough
          """;

    private static string GetFallback(string languageCode) => languageCode == "uz"
        ? "Sizga yordam berishga harakat qilaman! Moliyaviy xarajat yoki daromadingizni yozib qoldiring 💰"
        : "I'm here to help! Feel free to log your expenses or income 💰";
}
