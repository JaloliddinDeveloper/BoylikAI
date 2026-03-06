using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BoylikAI.Infrastructure.AI;

public sealed class ClaudeAdviceGenerator : IAdviceGenerator
{
    private readonly AnthropicClient _client;
    private readonly ILogger<ClaudeAdviceGenerator> _logger;
    private readonly AnthropicOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ClaudeAdviceGenerator(
        AnthropicClient client,
        IOptions<AnthropicOptions> options,
        ILogger<ClaudeAdviceGenerator> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<FinancialAdviceDto> GenerateAdviceAsync(
        Guid userId,
        FinancialHealthDto healthData,
        string languageCode,
        CancellationToken ct = default)
    {
        var prompt = BuildAdvicePrompt(healthData, languageCode);

        try
        {
            var request = new MessageParameters
            {
                Model = _options.AdviceModel,
                MaxTokens = 800,
                Messages = new List<Message>
                {
                    new(RoleType.User, prompt)
                },
                SystemMessage = GetSystemPrompt(languageCode)
            };

            var response = await _client.Messages.GetClaudeMessageAsync(request, ct);
            var content = response.Message.ToString() ?? string.Empty;

            // Structured JSON javob parse qilish
            var json = ExtractJson(content);
            if (!string.IsNullOrEmpty(json))
            {
                var structured = JsonSerializer.Deserialize<ClaudeAdviceResponse>(json, JsonOptions);
                if (structured is not null)
                {
                    return new FinancialAdviceDto(
                        Summary: structured.Summary ?? string.Empty,
                        ActionItems: structured.ActionItems ?? [],
                        Warnings: healthData.Warnings,
                        HealthScore: healthData.OverallScore,
                        LanguageCode: languageCode);
                }
            }

            // JSON parse bo'lmasa — fallback'ga o'tish
            _logger.LogWarning("Claude advice returned non-JSON response for user {UserId}", userId);
            return GetFallbackAdvice(healthData, languageCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate financial advice for user {UserId}", userId);
            return GetFallbackAdvice(healthData, languageCode);
        }
    }

    private static string GetSystemPrompt(string languageCode) => languageCode == "uz"
        ? """
          Siz shaxsiy moliyaviy maslahatchi botisiz. Foydalanuvchilarga moliyaviy holat haqida tushunarli maslahat berasiz.
          Maslahatlaringiz aniq, amaliy va do'stona ohangda bo'lishi kerak.

          MUHIM: Faqat JSON formatida javob bering. Boshqa hech narsa yozmang.
          JSON strukturasi:
          {
            "summary": "Bir jumlali xulosa",
            "action_items": ["1-tavsiya", "2-tavsiya", "3-tavsiya"]
          }
          """
        : """
          You are a personal financial advisor bot. Provide clear, actionable financial advice.
          Be supportive and specific. Keep language simple.

          IMPORTANT: Respond ONLY with JSON. No other text.
          JSON structure:
          {
            "summary": "One sentence summary",
            "action_items": ["Action 1", "Action 2", "Action 3"]
          }
          """;

    private static string BuildAdvicePrompt(FinancialHealthDto health, string languageCode)
    {
        var categoryBreakdown = string.Join("\n", health.CategoryRatios
            .Where(r => r.ActualPercentage > 0)
            .OrderByDescending(r => r.ActualPercentage)
            .Take(5)
            .Select(r => $"- {r.CategoryDisplayName}: {r.ActualPercentage:F1}% (tavsiya: {r.RecommendedPercentage:F1}%)"));

        var warningsList = health.Warnings.Count > 0
            ? string.Join("; ", health.Warnings)
            : (languageCode == "uz" ? "Yo'q" : "None");

        if (languageCode == "uz")
        {
            return $"""
                Quyidagi moliyaviy ma'lumotlar asosida maslahat bering:

                Daromad: {health.TotalIncome:N0} so'm
                Xarajat: {health.TotalExpenses:N0} so'm
                Jamg'arma: {health.SavingsAmount:N0} so'm ({health.SavingsRate:F1}%)
                Holat: {health.OverallScore}

                Top xarajat kategoriyalari:
                {categoryBreakdown}

                Ogohlantirishlar: {warningsList}

                Qisqa (3 ta) amaliy tavsiya bering.
                """;
        }

        return $"""
            Provide financial advice based on this data:

            Income: {health.TotalIncome:N0} UZS
            Expenses: {health.TotalExpenses:N0} UZS
            Savings: {health.SavingsAmount:N0} UZS ({health.SavingsRate:F1}%)
            Health: {health.OverallScore}

            Top expense categories:
            {categoryBreakdown}

            Warnings: {warningsList}

            Provide 3 concise, actionable recommendations.
            """;
    }

    private static string ExtractJson(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start >= 0 && end > start) return content[start..(end + 1)];
        return string.Empty;
    }

    private static FinancialAdviceDto GetFallbackAdvice(FinancialHealthDto health, string languageCode)
    {
        var isUz = languageCode == "uz";

        var summary = health.OverallScore switch
        {
            HealthScore.Excellent => isUz
                ? $"Ajoyib! Daromadingizning {health.SavingsRate:F1}% ini jamg'ardingiz."
                : $"Excellent! You saved {health.SavingsRate:F1}% of your income.",
            HealthScore.Good => isUz
                ? $"Yaxshi natija. {health.SavingsRate:F1}% jamg'arma qilindingiz."
                : $"Good job! {health.SavingsRate:F1}% saved this month.",
            HealthScore.Fair => isUz
                ? "Moliyaviy holat o'rtacha. Xarajatlarni ko'rib chiqing."
                : "Financial health is fair. Review your spending patterns.",
            _ => isUz
                ? "Xarajatlar daromaddan oshmoqda. Zudlik bilan choralar ko'ring."
                : "Expenses are exceeding income. Take immediate action."
        };

        var tips = health.OverallScore switch
        {
            HealthScore.Excellent => isUz
                ? new[] { "Jamg'armani investitsiyaga yo'naltiring.", "Favqulodda fond yarating.", "Moliyaviy maqsadlar belgilang." }
                : new[] { "Direct savings to investments.", "Build an emergency fund.", "Set long-term financial goals." },
            HealthScore.Good => isUz
                ? new[] { "Jamg'arma foizini 5% ga oshiring.", "Keraksiz obunalarni bekor qiling.", "Oylik byudjet tuzing." }
                : new[] { "Increase savings rate by 5%.", "Cancel unused subscriptions.", "Create a monthly budget." },
            _ => isUz
                ? new[] { "Eng katta xarajat kategoriyasini kamaytiring.", "Kunlik xarajat limitini belgilang.", "Daromadni oshirish yo'llarini qidiring." }
                : new[] { "Reduce your largest expense category.", "Set a daily spending limit.", "Look for ways to increase income." }
        };

        return new FinancialAdviceDto(summary, tips, health.Warnings, health.OverallScore, languageCode);
    }

    private sealed class ClaudeAdviceResponse
    {
        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("action_items")]
        public List<string>? ActionItems { get; set; }
    }
}
