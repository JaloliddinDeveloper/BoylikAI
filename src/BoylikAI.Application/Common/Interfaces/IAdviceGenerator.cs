using BoylikAI.Application.DTOs;

namespace BoylikAI.Application.Common.Interfaces;

public interface IAdviceGenerator
{
    Task<FinancialAdviceDto> GenerateAdviceAsync(
        Guid userId,
        FinancialHealthDto healthData,
        string languageCode,
        CancellationToken ct = default);
}
