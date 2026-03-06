namespace BoylikAI.Application.DTOs;

public sealed record SpendingPredictionDto(
    Guid UserId,
    int Year,
    int Month,
    decimal CurrentSpending,
    decimal PredictedMonthEndSpending,
    decimal MonthlyIncome,
    decimal ProjectedSavings,
    int DaysElapsed,
    int DaysInMonth,
    decimal AverageDailySpending,
    PredictionConfidence Confidence,
    string Warning);

public enum PredictionConfidence { Low, Medium, High }
