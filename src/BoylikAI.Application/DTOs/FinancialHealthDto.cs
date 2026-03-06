using BoylikAI.Domain.Enums;

namespace BoylikAI.Application.DTOs;

public sealed record FinancialHealthDto(
    Guid UserId,
    int Year,
    int Month,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal SavingsAmount,
    decimal SavingsRate,
    HealthScore OverallScore,
    IReadOnlyList<CategoryRatioDto> CategoryRatios,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Strengths);

public sealed record CategoryRatioDto(
    TransactionCategory Category,
    string CategoryDisplayName,
    decimal ActualPercentage,
    decimal RecommendedPercentage,
    RatioStatus Status);

public sealed record FinancialAdviceDto(
    string Summary,
    IReadOnlyList<string> ActionItems,
    IReadOnlyList<string> Warnings,
    HealthScore HealthScore,
    string LanguageCode);

public enum HealthScore { Critical, Poor, Fair, Good, Excellent }
public enum RatioStatus { UnderBudget, OnTrack, SlightlyOver, SignificantlyOver }
