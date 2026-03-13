using BoylikAI.Domain.Enums;

namespace BoylikAI.Application.DTOs;

public sealed record SpendingPredictionDto(
    Guid UserId,
    int  Year,
    int  Month,

    // ── Current state ─────────────────────────────────────────────────────
    decimal CurrentSpending,
    decimal MonthlyIncome,
    int     DaysElapsed,
    int     DaysInMonth,

    // ── Three-scenario forecast ───────────────────────────────────────────
    decimal PredictedMonthEndSpending,   // Ensemble base prediction
    decimal PredictedLow,                // Optimistic  (−1 σ × √remaining)
    decimal PredictedHigh,               // Pessimistic (+1 σ × √remaining)

    // ── Savings ───────────────────────────────────────────────────────────
    decimal ProjectedSavings,
    decimal ProjectedSavingsRate,        // %

    // ── Daily metrics ─────────────────────────────────────────────────────
    decimal AverageDailySpending,        // EWMA-smoothed daily rate
    decimal DailyBudgetRemaining,        // Max per remaining day to stay in budget
    decimal SpendingPacePercent,         // 100 = on track, >100 = ahead of budget

    // ── Trend ─────────────────────────────────────────────────────────────
    SpendingTrend Trajectory,
    decimal       TrendDeltaPerDay,      // OLS slope (+ = accelerating, - = decelerating)

    // ── Historical comparison ─────────────────────────────────────────────
    decimal? LastMonthTotalSpending,     // null = no prior-month data
    decimal? VsLastMonthPercent,         // +20 = 20 % more than same point last month

    // ── Confidence ────────────────────────────────────────────────────────
    PredictionConfidence Confidence,
    int                  ConfidencePercent,   // 0–95

    // ── Category-level predictions ────────────────────────────────────────
    IReadOnlyList<CategoryPredictionDto> CategoryPredictions,

    string Warning);

// ── Supporting types ──────────────────────────────────────────────────────

public enum PredictionConfidence { Low, Medium, High }
public enum SpendingTrend        { Improving, Stable, Worsening }

public sealed record CategoryPredictionDto(
    TransactionCategory Category,
    string              CategoryDisplayName,
    decimal             CurrentSpending,
    decimal             PredictedTotal,
    SpendingTrend       Trend);
