using BoylikAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Whisper.net;
using Whisper.net.Ggml;

namespace BoylikAI.Infrastructure.AI;

/// <summary>
/// Transcribes Telegram voice messages (OGG/Opus → WAV → text) using
/// the OpenAI Whisper model running locally — no API key required.
/// The GGML model file is downloaded once on first use and cached on disk.
/// </summary>
public sealed class WhisperTranscriptionService : IAudioTranscriptionService, IAsyncDisposable
{
    private readonly WhisperOptions _options;
    private readonly ILogger<WhisperTranscriptionService> _logger;

    private WhisperFactory? _factory;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public WhisperTranscriptionService(
        IOptions<WhisperOptions> options,
        ILogger<WhisperTranscriptionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default)
    {
        try
        {
            await EnsureInitializedAsync(ct);

            if (_factory is null)
            {
                _logger.LogWarning("Whisper factory not available — voice transcription skipped");
                return null;
            }

            // Convert OGG/Opus to WAV in memory using ffmpeg
            var wavBytes = await ConvertToWavAsync(audioStream, ct);
            if (wavBytes is null) return null;

            using var processor = _factory.CreateBuilder()
                .WithLanguage(_options.Language)
                .Build();

            var segments = new List<string>();
            await foreach (var segment in processor.ProcessAsync(new MemoryStream(wavBytes), ct))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    segments.Add(segment.Text.Trim());
            }

            var result = string.Join(" ", segments).Trim();
            _logger.LogInformation("Whisper transcribed {Chars} chars from {File}", result.Length, fileName);

            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice transcription failed for {File}", fileName);
            return null;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var modelPath = _options.ModelPath;

            if (!File.Exists(modelPath))
            {
                _logger.LogInformation(
                    "Downloading Whisper model {Type} to {Path} (one-time ~{SizeMb} MB)...",
                    _options.ModelType, modelPath, GetModelSizeMb(_options.ModelType));

                var dir = Path.GetDirectoryName(modelPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                await using var modelStream = await WhisperGgmlDownloader
                    .GetGgmlModelAsync(_options.ModelType);
                await using var file = File.OpenWrite(modelPath);
                await modelStream.CopyToAsync(file, ct);

                _logger.LogInformation("Whisper model downloaded successfully");
            }

            _factory = WhisperFactory.FromPath(modelPath);
            _initialized = true;

            _logger.LogInformation("Whisper factory initialized with model {Path}", modelPath);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Converts OGG/Opus audio (from Telegram) to WAV using ffmpeg.
    /// ffmpeg must be installed on the server: apt-get install ffmpeg
    /// </summary>
    private async Task<byte[]?> ConvertToWavAsync(Stream inputStream, CancellationToken ct)
    {
        try
        {
            var tempInput  = Path.GetTempFileName() + ".ogg";
            var tempOutput = Path.GetTempFileName() + ".wav";

            try
            {
                await using (var fs = File.OpenWrite(tempInput))
                    await inputStream.CopyToAsync(fs, ct);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName  = "ffmpeg",
                    Arguments = $"-y -i \"{tempInput}\" -ar 16000 -ac 1 -f wav \"{tempOutput}\"",
                    RedirectStandardError  = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow  = true
                };

                using var proc = System.Diagnostics.Process.Start(psi)
                    ?? throw new InvalidOperationException("ffmpeg process could not be started");

                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode != 0)
                {
                    var err = await proc.StandardError.ReadToEndAsync(ct);
                    _logger.LogWarning("ffmpeg exited {Code}: {Error}", proc.ExitCode, err);
                    return null;
                }

                return await File.ReadAllBytesAsync(tempOutput, ct);
            }
            finally
            {
                if (File.Exists(tempInput))  File.Delete(tempInput);
                if (File.Exists(tempOutput)) File.Delete(tempOutput);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OGG→WAV conversion failed");
            return null;
        }
    }

    private static int GetModelSizeMb(GgmlType type) => type switch
    {
        GgmlType.Tiny    => 75,
        GgmlType.Base    => 142,
        GgmlType.Small   => 466,
        GgmlType.Medium  => 1500,
        GgmlType.LargeV3 => 3100,
        _                => 142
    };

    public async ValueTask DisposeAsync()
    {
        _factory?.Dispose();
        _initLock.Dispose();
        await ValueTask.CompletedTask;
    }
}
