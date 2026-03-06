namespace BoylikAI.Infrastructure.AI;

public sealed class AnthropicOptions
{
    public const string SectionName = "Anthropic";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Tranzaksiya parse qilish uchun model. Haiku — tezkor va arzon.</summary>
    public string ParserModel { get; set; } = "claude-haiku-4-5-20251001";

    /// <summary>Moliyaviy maslahat uchun model.</summary>
    public string AdviceModel { get; set; } = "claude-haiku-4-5-20251001";

    /// <summary>Foydalanuvchi xabarining maksimal uzunligi (prompt injection himoyasi).</summary>
    public int MaxInputLength { get; set; } = 500;

    /// <summary>Mantiqiy maksimal tranzaksiya miqdori (1 milliard so'm).</summary>
    public decimal MaxReasonableAmount { get; set; } = 1_000_000_000m;

    /// <summary>Foydalanuvchi minutiga qilishi mumkin bo'lgan maksimal AI so'rovlar.</summary>
    public int PerUserRateLimitPerMinute { get; set; } = 10;
}
