using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BoylikAI.Infrastructure.Analytics;

/// <summary>
/// Moliyaviy analytics engine. DB-level aggregation ishlatadi (in-memory emas).
/// Barcha hisob-kitoblar optimallashtirilgan.
/// </summary>
public sealed class AnalyticsEngine : IAnalyticsEngine
{
    private readonly ITransactionRepository _txRepo;
    private readonly ILogger<AnalyticsEngine> _logger;

    // Tavsiya etilgan xarajat nisbatlari (50/30/20 — O'zbekistonga moslashtirilgan)
    // JAMI: 100% (avvalgi 115% xatosi tuzatildi)
    private static readonly Dictionary<TransactionCategory, decimal> RecommendedRatios = new()
    {
        [TransactionCategory.Food]          = 25.0m,
        [TransactionCategory.Housing]       = 15.0m,  // 20 → 15
        [TransactionCategory.Transport]     = 10.0m,
        [TransactionCategory.Bills]         = 10.0m,
        [TransactionCategory.Shopping]      = 10.0m,
        [TransactionCategory.Health]        = 5.0m,
        [TransactionCategory.Education]     = 5.0m,
        [TransactionCategory.Entertainment] = 5.0m,
        [TransactionCategory.Other]         = 5.0m,
        [TransactionCategory.Savings]       = 10.0m,  // 20 → 10 (hisob uchun)
    };
    // Jami: 100% ✓

    public AnalyticsEngine(ITransactionRepository txRepo, ILogger<AnalyticsEngine> logger)
    {
        _txRepo = txRepo;
        _logger = logger;
    }

    public async Task<AnalyticsReportDto> GetDailyReportAsync(
        Guid userId, DateOnly date, CancellationToken ct = default)
    {
        var transactions = await _txRepo.GetByUserIdAndDateRangeAsync(userId, date, date, ct);
        return BuildReport(userId, transactions, ReportPeriod.Daily, date, date);
    }

    public async Task<AnalyticsReportDto> GetWeeklyReportAsync(
        Guid userId, DateOnly weekStart, CancellationToken ct = default)
    {
        var weekEnd = weekStart.AddDays(6);
        var transactions = await _txRepo.GetByUserIdAndDateRangeAsync(userId, weekStart, weekEnd, ct);
        return BuildReport(userId, transactions, ReportPeriod.Weekly, weekStart, weekEnd);
    }

    public async Task<AnalyticsReportDto> GetMonthlyReportAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        // DB-level aggregation — barcha tranzaksiyalarni xotiraga yuklamaydi
        var (income, expenses) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);
        var categoryData = await _txRepo.GetMonthlyCategorySummaryAsync(
            userId, year, month, TransactionType.Expense, ct);

        var from = new DateOnly(year, month, 1);
        var to = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var totalExpenses = categoryData.Sum(c => c.TotalAmount);
        var categoryBreakdown = categoryData
            .Select(c =>
            {
                var amount = Math.Round(c.TotalAmount, 0);
                return new CategoryBreakdownDto(
                    c.Category,
                    GetCategoryDisplayName(c.Category),
                    amount,
                    totalExpenses > 0 ? Math.Round((amount / totalExpenses) * 100, 1) : 0,
                    c.Count);
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        return new AnalyticsReportDto(
            userId, ReportPeriod.Monthly, from, to,
            Math.Round(income, 0),
            Math.Round(expenses, 0),
            Math.Round(income - expenses, 0),
            "UZS",
            categoryBreakdown,
            DateTimeOffset.UtcNow);
    }

    public async Task<SpendingPredictionDto> PredictMonthEndSpendingAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        bool isCurrentMonth = today.Year == year && today.Month == month;

        // O'tgan oy uchun prognoz ma'nosiz — actual ma'lumot qaytariladi
        if (!isCurrentMonth)
        {
            var (historicIncome, historicExpenses) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);
            return new SpendingPredictionDto(
                userId, year, month,
                Math.Round(historicExpenses, 0),
                Math.Round(historicExpenses, 0),
                Math.Round(historicIncome, 0),
                Math.Round(historicIncome - historicExpenses, 0),
                daysInMonth, daysInMonth,
                daysInMonth > 0 ? Math.Round(historicExpenses / daysInMonth, 0) : 0,
                PredictionConfidence.High,
                "Bu oy tugagan — haqiqiy ma'lumotlar ko'rsatilmoqda.");
        }

        var daysElapsed = today.Day;
        var (income, _) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);
        var transactions = await _txRepo.GetByUserIdAndMonthAsync(userId, year, month, ct);
        var expenses = transactions.Where(t => t.Type == TransactionType.Expense).ToList();
        var currentSpending = expenses.Sum(t => t.Amount.Amount);

        if (daysElapsed == 0)
        {
            return new SpendingPredictionDto(
                userId, year, month, 0, 0, income, income, 0, daysInMonth, 0,
                PredictionConfidence.Low, string.Empty);
        }

        // So'nggi 7 kun — faqat JORIY OY tranzaksiyalari (avvalgi oy muammosi tuzatildi)
        var monthStart = new DateOnly(year, month, 1);
        var sevenDaysAgo = today.AddDays(-6); // today inclusive = 7 days
        var windowStart = sevenDaysAgo < monthStart ? monthStart : sevenDaysAgo;
        var windowDays = (today.DayNumber - windowStart.DayNumber) + 1;

        var recentExpenses = expenses
            .Where(t => t.TransactionDate >= windowStart && t.TransactionDate <= today)
            .Sum(t => t.Amount.Amount);

        var movingAvgDailyRate = windowDays > 0 ? recentExpenses / windowDays : 0;
        var overallDailyRate = currentSpending / daysElapsed;

        // Weighted: so'nggi trenzd ko'proq og'irlik oladi
        var weightedDailyRate = (movingAvgDailyRate * 0.6m) + (overallDailyRate * 0.4m);

        var remainingDays = daysInMonth - daysElapsed;
        var predictedTotal = currentSpending + (weightedDailyRate * remainingDays);
        var projectedSavings = income - predictedTotal;

        var confidence = daysElapsed >= 7 ? PredictionConfidence.High
            : daysElapsed >= 3 ? PredictionConfidence.Medium
            : PredictionConfidence.Low;

        var warning = BuildPredictionWarning(predictedTotal, income);

        return new SpendingPredictionDto(
            userId, year, month,
            Math.Round(currentSpending, 0),
            Math.Round(predictedTotal, 0),
            Math.Round(income, 0),
            Math.Round(projectedSavings, 0),
            daysElapsed, daysInMonth,
            Math.Round(weightedDailyRate, 0),
            confidence, warning);
    }

    public async Task<FinancialHealthDto> GetFinancialHealthAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        // DB-level aggregation — ham income, ham expense alohida query
        var (totalIncome, totalExpenses) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);
        var categoryData = await _txRepo.GetMonthlyCategorySummaryAsync(
            userId, year, month, TransactionType.Expense, ct);

        var categoryAmounts = categoryData.ToDictionary(c => c.Category, c => c.TotalAmount);

        var savings = totalIncome - totalExpenses;
        var savingsRate = totalIncome > 0
            ? Math.Round((savings / totalIncome) * 100, 1)
            : 0m;

        var ratios = BuildCategoryRatios(categoryAmounts, totalIncome);
        var warnings = BuildWarnings(categoryAmounts, totalIncome, totalExpenses, savingsRate);
        var strengths = BuildStrengths(savingsRate);
        var healthScore = CalculateHealthScore(totalIncome, totalExpenses, savingsRate, warnings.Count);

        return new FinancialHealthDto(
            userId, year, month,
            Math.Round(totalIncome, 0),
            Math.Round(totalExpenses, 0),
            Math.Round(savings, 0),
            savingsRate,
            healthScore, ratios, warnings, strengths);
    }

    // ────────────────────────────────────────────────────────────
    // PRIVATE HELPERS
    // ────────────────────────────────────────────────────────────

    private static AnalyticsReportDto BuildReport(
        Guid userId,
        IEnumerable<Domain.Entities.Transaction> transactions,
        ReportPeriod period,
        DateOnly from,
        DateOnly to)
    {
        var txList = transactions.ToList();
        var totalExpenses = txList.Where(t => t.Type == TransactionType.Expense)
                                   .Sum(t => t.Amount.Amount);
        var totalIncome = txList.Where(t => t.Type == TransactionType.Income)
                                 .Sum(t => t.Amount.Amount);

        // g.Sum() bir marta hisoblanadi — avvalgi ikki marta hisoblash xatosi tuzatildi
        var categoryBreakdown = txList
            .Where(t => t.Type == TransactionType.Expense)
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                var amount = Math.Round(g.Sum(t => t.Amount.Amount), 0);
                return new CategoryBreakdownDto(
                    g.Key,
                    GetCategoryDisplayName(g.Key),
                    amount,
                    totalExpenses > 0 ? Math.Round((amount / totalExpenses) * 100, 1) : 0,
                    g.Count());
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var currency = txList.FirstOrDefault()?.Amount.Currency ?? "UZS";

        return new AnalyticsReportDto(
            userId, period, from, to,
            Math.Round(totalIncome, 0),
            Math.Round(totalExpenses, 0),
            Math.Round(totalIncome - totalExpenses, 0),
            currency,
            categoryBreakdown,
            DateTimeOffset.UtcNow);
    }

    private static IReadOnlyList<CategoryRatioDto> BuildCategoryRatios(
        Dictionary<TransactionCategory, decimal> categoryAmounts,
        decimal totalIncome)
    {
        return RecommendedRatios
            .Where(kvp => IsExpenseCategory(kvp.Key))
            .Select(kvp =>
            {
                var (category, recommended) = kvp;
                var actual = categoryAmounts.GetValueOrDefault(category, 0);
                var actualPct = totalIncome > 0
                    ? Math.Round((actual / totalIncome) * 100, 1)
                    : 0m;

                var status = actualPct switch
                {
                    var p when p <= recommended * 0.9m => RatioStatus.UnderBudget,
                    var p when p <= recommended * 1.1m => RatioStatus.OnTrack,
                    var p when p <= recommended * 1.3m => RatioStatus.SlightlyOver,
                    _ => RatioStatus.SignificantlyOver
                };

                return new CategoryRatioDto(
                    category, GetCategoryDisplayName(category),
                    actualPct, recommended, status);
            })
            .ToList();
    }

    private static List<string> BuildWarnings(
        Dictionary<TransactionCategory, decimal> categoryAmounts,
        decimal totalIncome,
        decimal totalExpenses,
        decimal savingsRate)
    {
        var warnings = new List<string>();

        if (totalExpenses > totalIncome)
            warnings.Add("Xarajatlar daromaddan oshib ketdi!");

        if (totalIncome > 0 && savingsRate < 10)
            warnings.Add("Jamg'arma daromadning 10% dan kam.");

        // Faqat EXPENSE kategoriyalari tekshiriladi (income emas!)
        foreach (var (category, amount) in categoryAmounts)
        {
            if (!IsExpenseCategory(category)) continue;  // Income categoriyalarni o'tkazib yuboramiz
            if (!RecommendedRatios.TryGetValue(category, out var recommended)) continue;
            if (totalIncome <= 0) continue;

            var pct = (amount / totalIncome) * 100;
            if (pct > recommended * 1.5m)
                warnings.Add($"{GetCategoryDisplayName(category)} xarajatlari tavsiyadan {pct / recommended:F1}x ko'p.");
        }

        return warnings;
    }

    private static List<string> BuildStrengths(decimal savingsRate)
    {
        var strengths = new List<string>();
        if (savingsRate >= 20)
            strengths.Add($"Ajoyib! Daromadingizning {savingsRate:F1}% ni jamg'ardingiz.");
        else if (savingsRate >= 10)
            strengths.Add($"Yaxshi! {savingsRate:F1}% jamg'arma qilindingiz.");
        return strengths;
    }

    private static HealthScore CalculateHealthScore(
        decimal totalIncome, decimal totalExpenses, decimal savingsRate, int warningCount)
    {
        // Daromad = 0 holati: faqat xarajat kiritilganda — critical
        if (totalIncome == 0)
            return totalExpenses > 0 ? HealthScore.Critical : HealthScore.Fair;

        var score = 100;

        // Ogohlantirishlar uchun penalti
        score -= warningCount * 15;

        // Jamg'arma bonuslari/penaltilari
        if (savingsRate < 0) score -= 30;
        else if (savingsRate < 5) score -= 20;
        else if (savingsRate < 10) score -= 10;
        else if (savingsRate >= 20) score += 10;

        // Score 0-110 orasida bo'lishi kerak
        score = Math.Clamp(score, 0, 110);

        return score switch
        {
            >= 90 => HealthScore.Excellent,
            >= 70 => HealthScore.Good,
            >= 50 => HealthScore.Fair,
            >= 30 => HealthScore.Poor,
            _ => HealthScore.Critical
        };
    }

    private static string BuildPredictionWarning(decimal predicted, decimal income)
    {
        if (income <= 0) return string.Empty;

        var ratio = predicted / income;
        return ratio switch
        {
            > 1.2m => "Xarajatlar daromaddan sezilarli darajada oshib ketishi kutilmoqda!",
            > 1.0m => "Xarajatlar daromaddan oshib ketishi taxmin qilinmoqda.",
            > 0.9m => "Xarajatlar daromadga yaqinlashmoqda. Ehtiyot bo'ling.",
            _ => string.Empty
        };
    }

    private static bool IsExpenseCategory(TransactionCategory category) =>
        category is not (TransactionCategory.Salary or TransactionCategory.Freelance
            or TransactionCategory.Investment or TransactionCategory.Savings);

    internal static string GetCategoryDisplayName(TransactionCategory category) => category switch
    {
        TransactionCategory.Transport     => "Transport",
        TransactionCategory.Food          => "Oziq-ovqat",
        TransactionCategory.Shopping      => "Xarid",
        TransactionCategory.Bills         => "To'lovlar",
        TransactionCategory.Entertainment => "Ko'ngil ochish",
        TransactionCategory.Health        => "Salomatlik",
        TransactionCategory.Education     => "Ta'lim",
        TransactionCategory.Housing       => "Uy-joy",
        TransactionCategory.Savings       => "Jamg'arma",
        TransactionCategory.Salary        => "Oylik",
        TransactionCategory.Freelance     => "Freelance",
        TransactionCategory.Investment    => "Investitsiya",
        TransactionCategory.Other         => "Boshqa",
        _ => category.ToString()
    };
}
