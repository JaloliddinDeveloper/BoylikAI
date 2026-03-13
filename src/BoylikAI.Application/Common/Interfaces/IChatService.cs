namespace BoylikAI.Application.Common.Interfaces;

public interface IChatService
{
    Task<string> ChatAsync(string message, string languageCode, CancellationToken ct = default);
}
