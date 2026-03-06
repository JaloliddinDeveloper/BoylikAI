using BoylikAI.Application.DTOs;

namespace BoylikAI.Application.Common.Interfaces;

public interface ITransactionParser
{
    /// <summary>
    /// Parses a natural language message (Uzbek or Russian) into a structured ParsedTransactionDto.
    /// Uses LLM-backed semantic understanding with rule-based fallback.
    /// </summary>
    Task<ParsedTransactionDto?> ParseAsync(string message, string userId, CancellationToken ct = default);
}
