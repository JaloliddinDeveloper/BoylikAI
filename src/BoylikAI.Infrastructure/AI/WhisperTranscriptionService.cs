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

            var wavBytes = await ConvertToWavAsync(audioStream, ct);
            if (wavBytes is null) return null;

            _logger.LogDebug("Running Whisper inference on {Bytes} bytes of WAV audio", wavBytes.Length);

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

            if (string.IsNullOrWhiteSpace(result))
            {
                _logger.LogWarning("Whisper returned empty transcription for {File}", fileName);
                return null;
            }

            _logger.LogInformation("Whisper transcribed: \"{Text}\"", result);
            return result;
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

            _logger.LogInformation("Whisper model path: {Path}", Path.GetFullPath(modelPath));

            if (!File.Exists(modelPath))
            {
                _logger.LogInformation(
                    "Downloading Whisper '{Type}' model to {Path} (~{Mb} MB, one-time)...",
                    _options.ModelType, modelPath, GetModelSizeMb(_options.ModelType));

                var dir = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
                Directory.CreateDirectory(dir);

                var tempPath = modelPath + ".tmp";
                try
                {
                    await using var modelStream = await WhisperGgmlDownloader
                        .GetGgmlModelAsync(_options.ModelType);
                    await using var file = File.OpenWrite(tempPath);
                    await modelStream.CopyToAsync(file, ct);
                }
                catch
                {
                    // Yarim yuklangan faylni o'chirib tashlash
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    throw;
                }

                File.Move(tempPath, modelPath, overwrite: true);
                _logger.LogInformation("Whisper model downloaded: {Path}", modelPath);
            }
            else
            {
                var size = new FileInfo(modelPath).Length / 1024 / 1024;
                _logger.LogInformation("Whisper model found: {Path} ({Mb} MB)", modelPath, size);
            }

            _factory = WhisperFactory.FromPath(modelPath);
            _initialized = true;

            _logger.LogInformation("Whisper factory initialized — language: {Lang}", _options.Language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper initialization failed — voice messages will not work");
            // _factory stays null; TranscribeAsync will return null gracefully
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Converts OGG/Opus (Telegram voice) to WAV 16kHz mono using ffmpeg.
    /// ffmpeg must be in PATH: on Docker it's installed in the Dockerfile.
    /// On Windows dev: install from https://ffmpeg.org/download.html
    /// </summary>
    private async Task<byte[]?> ConvertToWavAsync(Stream inputStream, CancellationToken ct)
    {
        var tempInput  = Path.Combine(Path.GetTempPath(), $"voice_{Guid.NewGuid():N}.ogg");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"voice_{Guid.NewGuid():N}.wav");

        try
        {
            await using (var fs = File.OpenWrite(tempInput))
                await inputStream.CopyToAsync(fs, ct);

            _logger.LogDebug("Converting OGG→WAV: {In} → {Out}", tempInput, tempOutput);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName  = "ffmpeg",
                Arguments = $"-y -i \"{tempInput}\" -ar 16000 -ac 1 -f wav \"{tempOutput}\"",
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow  = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
            {
                _logger.LogError("ffmpeg not found — install it: apt-get install ffmpeg (Linux) or https://ffmpeg.org (Windows)");
                return null;
            }

            // Deadlock oldini olish: stderr ni parallel o'qish
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                _logger.LogWarning("ffmpeg exit {Code}: {Error}", proc.ExitCode, stderr);
                return null;
            }

            if (!File.Exists(tempOutput) || new FileInfo(tempOutput).Length == 0)
            {
                _logger.LogWarning("ffmpeg produced empty/missing output file");
                return null;
            }

            var wav = await File.ReadAllBytesAsync(tempOutput, ct);
            _logger.LogDebug("OGG→WAV conversion done: {Bytes} bytes", wav.Length);
            return wav;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OGG→WAV conversion failed");
            return null;
        }
        finally
        {
            if (File.Exists(tempInput))  File.Delete(tempInput);
            if (File.Exists(tempOutput)) File.Delete(tempOutput);
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
