namespace BoylikAI.Application.Common.Interfaces;

public interface IAudioTranscriptionService
{
    /// <summary>
    /// Transcribes an audio stream (OGG/Opus from Telegram voice message) to text.
    /// Returns null if transcription fails or audio is empty.
    /// </summary>
    Task<string?> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default);
}
