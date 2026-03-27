using Whisper.net.Ggml;

namespace BoylikAI.Infrastructure.AI;

public sealed class WhisperOptions
{
    public const string SectionName = "Whisper";

    /// <summary>
    /// Path where the GGML model file will be stored.
    /// Default: /app/models/whisper-base.bin
    /// </summary>
    public string ModelPath { get; init; } = "/app/models/whisper-base.bin";

    /// <summary>
    /// Whisper GGML model type. Tiny/Base — tez, Small/Medium — aniqroq.
    /// Default: Base (142 MB, o'zbek/rus uchun yetarli)
    /// </summary>
    public GgmlType ModelType { get; init; } = GgmlType.Base;

    /// <summary>
    /// Primary language hint for Whisper. "uz" yoki "ru".
    /// </summary>
    public string Language { get; init; } = "uz";
}
