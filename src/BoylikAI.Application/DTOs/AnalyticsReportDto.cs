using BoylikAI.Domain.Enums;

namespace BoylikAI.Application.DTOs;

public sealed record AnalyticsReportDto(
    Guid UserId,
    ReportPeriod Period,
    DateOnly From,
    DateOnly To,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetBalance,
    string Currency,
    IReadOnlyList<CategoryBreakdownDto> CategoryBreakdown,
    DateTimeOffset GeneratedAt);

public sealed record CategoryBreakdownDto(
    TransactionCategory Category,
    string CategoryDisplayName,
    decimal Amount,
    decimal Percentage,
    int TransactionCount);

public enum ReportPeriod { Daily, Weekly, Monthly }
