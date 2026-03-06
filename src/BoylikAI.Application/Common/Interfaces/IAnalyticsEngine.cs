using BoylikAI.Application.DTOs;

namespace BoylikAI.Application.Common.Interfaces;

public interface IAnalyticsEngine
{
    Task<AnalyticsReportDto> GetDailyReportAsync(Guid userId, DateOnly date, CancellationToken ct = default);
    Task<AnalyticsReportDto> GetWeeklyReportAsync(Guid userId, DateOnly weekStart, CancellationToken ct = default);
    Task<AnalyticsReportDto> GetMonthlyReportAsync(Guid userId, int year, int month, CancellationToken ct = default);
    Task<SpendingPredictionDto> PredictMonthEndSpendingAsync(Guid userId, int year, int month, CancellationToken ct = default);
    Task<FinancialHealthDto> GetFinancialHealthAsync(Guid userId, int year, int month, CancellationToken ct = default);
}
