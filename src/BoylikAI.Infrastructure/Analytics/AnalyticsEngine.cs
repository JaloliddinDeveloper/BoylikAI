using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BoylikAI.Infrastructure.Analytics;

/// <summary>
/// Moliyaviy analytics engine.
/// Prognoz uchun uch xil signal ensemble ishlatadi:
///   1. EWMA  — exponential weighted moving average (α=0.25)
///   2. OLS   — ordinary least-squares linear regression trend
///   3. Hist  — o'tgan oy bilan nisbiy taqqoslash
/// Dinamik og'irliklar (oy boshida historik og'ir, o'rtasida regression)
/// va dispersiyaga asoslangan ishonch oralig'i hisoblanadi.
/// </summary>
public sealed class AnalyticsEngine : IAnalyticsEngine
{
    private readonly ITransactionRepository _txRepo;
    private readonly ILogger<AnalyticsEngine> _logger;

    // Tavsiya etilgan xarajat nisbatlari (O'zbekistonga moslashtirilgan, jami 100%)
    private static readonly Dictionary<TransactionCategory, decimal> RecommendedRatios = new()
    {
        [TransactionCategory.Food]          = 25.0m,
        [TransactionCategory.Housing]       = 15.0m,
        [TransactionCategory.Transport]     = 10.0m,
        [TransactionCategory.Bills]         = 10.0m,
        [TransactionCategory.Shopping]      = 10.0m,
        [TransactionCategory.Health]        =  5.0m,
        [TransactionCategory.Education]     =  5.0m,
        [TransactionCategory.Entertainment] =  5.0m,
        [TransactionCategory.Other]         =  5.0m,
        [TransactionCategory.Savings]       = 10.0m,
    };

    public AnalyticsEngine(ITransactionRepository txRepo, ILogger<AnalyticsEngine> logger)
    {
        _txRepo = txRepo;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

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
        var (income, expenses) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);
        var categoryData = await _txRepo.GetMonthlyCategorySummaryAsync(
            userId, year, month, TransactionType.Expense, ct);

        var from = new DateOnly(year, month, 1);
        var to   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        var totalExpenses = categoryData.Sum(c => c.TotalAmount);
        var categoryBreakdown = categoryData
            .Select(c =>
            {
                var amount = Math.Round(c.TotalAmount, 0);
                return new CategoryBreakdownDto(
                    c.Category,
                    GetCategoryDisplayName(c.Category),
                    amount,
                    totalExpenses > 0 ? Math.Round(amount / totalExpenses * 100, 1) : 0,
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

    // ── Professional multi-signal prediction ─────────────────────────────

    public async Task<SpendingPredictionDto> PredictMonthEndSpendingAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        var today      = DateOnly.FromDateTime(DateTime.UtcNow);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var isCurrentMonth = today.Year == year && today.Month == month;

        // O'tgan oy — haqiqiy ma'lumotlar qaytariladi
        if (!isCurrentMonth)
        {
            var (hIncome, hExpenses) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);
            return BuildPastMonthResult(userId, year, month, daysInMonth, hIncome, hExpenses);
        }

        var daysElapsed = today.Day;
        var (income, _) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);

        var txs      = await _txRepo.GetByUserIdAndMonthAsync(userId, year, month, ct);
        var expenses = txs.Where(t => t.Type == TransactionType.Expense).ToList();
        var currentSpending = expenses.Sum(t => t.Amount.Amount);

        if (!expenses.Any())
            return BuildNoDataResult(userId, year, month, income, daysInMonth, daysElapsed);

        // ── 1. Kunlik massiv (index 0 = 1-kun, index n-1 = bugun) ────────
        var daily = BuildDailyArray(expenses, daysElapsed);

        // ── 2. Signal A: EWMA (α=0.25) ──────────────────────────────────
        //    Yaqin kunlarga ko'proq og'irlik, lekin yumshoq silliqlash
        var ewmaRate = ComputeEwma(daily, alpha: 0.25m);

        // ── 3. Signal B: OLS linear regression ──────────────────────────
        //    slope > 0: xarajat oshib borayapti; slope < 0: kamaymoqda
        var (slope, intercept) = ComputeOlsRegression(daily);
        var regressionRemaining = PredictRemainingByRegression(slope, intercept, daysElapsed, daysInMonth);

        // ── 4. Signal C: O'tgan oy bilan nisbiy taqqos ──────────────────
        var (lastMonthTotal, lastMonthAtSameDay) =
            await GetLastMonthComparisonAsync(userId, year, month, daysElapsed, ct);

        decimal? vsLastMonthPct = null;
        decimal  histRemaining  = 0;

        if (lastMonthTotal > 0 && lastMonthAtSameDay > 0)
        {
            vsLastMonthPct = Math.Round((currentSpending / lastMonthAtSameDay - 1) * 100, 1);
            // Hozirgi sur'at asosida o'tgan oyning qolgan qismini o'lchaymiz
            var paceRatio        = currentSpending / lastMonthAtSameDay;
            var lastRemaining    = lastMonthTotal - lastMonthAtSameDay;
            histRemaining        = Math.Max(0, lastRemaining * paceRatio);
        }

        var remainingDays = daysInMonth - daysElapsed;

        // ── 5. Ensemble: dinamik og'irliklar ─────────────────────────────
        //    Oy boshida → tarixga tayanamiz (noaniq)
        //    O'rtasida  → muvozanat
        //    Oxirida    → regression ishonchli
        decimal predictedRemaining;

        if (lastMonthTotal > 0)
        {
            var (wE, wR, wH) = daysElapsed switch
            {
                <= 3  => (0.20m, 0.10m, 0.70m),
                <= 7  => (0.35m, 0.15m, 0.50m),
                <= 14 => (0.45m, 0.25m, 0.30m),
                _     => (0.40m, 0.35m, 0.25m)
            };
            predictedRemaining =
                ewmaRate * remainingDays * wE +
                regressionRemaining       * wR +
                histRemaining             * wH;
        }
        else
        {
            var (wE, wR) = daysElapsed <= 7 ? (0.70m, 0.30m) : (0.50m, 0.50m);
            predictedRemaining =
                ewmaRate * remainingDays * wE +
                regressionRemaining      * wR;
        }

        var predictedTotal = currentSpending + Math.Max(0, predictedRemaining);

        // ── 6. Ishonch oralig'i — dispersiyaga asoslangan ────────────────
        //    interval = σ × √(qolgan kun), ≈ 68 % ehtimollik oralig'i
        var variance     = ComputeVariance(daily);
        var stdDev       = (decimal)Math.Sqrt((double)variance);
        var intervalWidth = stdDev * (decimal)Math.Sqrt(Math.Max(1, remainingDays)) * 1.1m;
        var predictedLow  = Math.Max(currentSpending, predictedTotal - intervalWidth);
        var predictedHigh = predictedTotal + intervalWidth;

        // ── 7. Ishonch foizi ─────────────────────────────────────────────
        var confPct    = ComputeConfidencePct(daysElapsed, lastMonthTotal, stdDev, ewmaRate);
        var confidence = confPct >= 75 ? PredictionConfidence.High
                       : confPct >= 45 ? PredictionConfidence.Medium
                       : PredictionConfidence.Low;

        // ── 8. Sarflash trendi ───────────────────────────────────────────
        var trajectory = ComputeTrajectory(slope, ewmaRate);

        // ── 9. Sarflash tempi (ideal chiziqli sur'atga nisbatan) ─────────
        var expectedLinear = income > 0
            ? income * (decimal)daysElapsed / daysInMonth
            : ewmaRate * daysElapsed;
        var pacePct = expectedLinear > 0
            ? Math.Round(currentSpending / expectedLinear * 100, 1)
            : 100m;

        // ── 10. Qolgan kunlarga kunlik byudjet ───────────────────────────
        var dailyBudgetRemaining = remainingDays > 0 && income > currentSpending
            ? Math.Round((income - currentSpending) / remainingDays, 0)
            : 0;

        var projectedSavings     = income - predictedTotal;
        var projectedSavingsRate = income > 0
            ? Math.Round(projectedSavings / income * 100, 1)
            : 0m;

        // ── 11. Kategoriya bo'yicha prognoz ──────────────────────────────
        var categoryPredictions = BuildCategoryPredictions(expenses, daysElapsed, daysInMonth);

        var warning = BuildEnhancedWarning(predictedTotal, income, pacePct, trajectory);

        return new SpendingPredictionDto(
            userId, year, month,
            Math.Round(currentSpending, 0),
            Math.Round(income, 0),
            daysElapsed, daysInMonth,
            Math.Round(predictedTotal, 0),
            Math.Round(predictedLow,   0),
            Math.Round(predictedHigh,  0),
            Math.Round(projectedSavings, 0),
            projectedSavingsRate,
            Math.Round(ewmaRate, 0),
            dailyBudgetRemaining,
            pacePct,
            trajectory,
            Math.Round(slope, 0),
            lastMonthTotal > 0 ? Math.Round(lastMonthTotal, 0) : null,
            vsLastMonthPct,
            confidence,
            confPct,
            categoryPredictions,
            warning);
    }

    public async Task<FinancialHealthDto> GetFinancialHealthAsync(
        Guid userId, int year, int month, CancellationToken ct = default)
    {
        var (totalIncome, totalExpenses) = await _txRepo.GetMonthlyTotalsAsync(userId, year, month, ct);
        var categoryData = await _txRepo.GetMonthlyCategorySummaryAsync(
            userId, year, month, TransactionType.Expense, ct);

        var categoryAmounts = categoryData.ToDictionary(c => c.Category, c => c.TotalAmount);

        var savings     = totalIncome - totalExpenses;
        var savingsRate = totalIncome > 0
            ? Math.Round(savings / totalIncome * 100, 1)
            : 0m;

        var ratios      = BuildCategoryRatios(categoryAmounts, totalIncome);
        var warnings    = BuildWarnings(categoryAmounts, totalIncome, totalExpenses, savingsRate);
        var strengths   = BuildStrengths(savingsRate);
        var healthScore = CalculateHealthScore(totalIncome, totalExpenses, savingsRate, warnings.Count);

        return new FinancialHealthDto(
            userId, year, month,
            Math.Round(totalIncome, 0),
            Math.Round(totalExpenses, 0),
            Math.Round(savings, 0),
            savingsRate,
            healthScore, ratios, warnings, strengths);
    }

    // ── Algorithm helpers (pure static) ──────────────────────────────────

    /// <summary>
    /// Kunlik xarajatlar massivi (0-indekslangan, i = kun_nomer - 1).
    /// </summary>
    private static decimal[] BuildDailyArray(
        IEnumerable<Domain.Entities.Transaction> expenses, int daysElapsed)
    {
        var arr = new decimal[daysElapsed];
        foreach (var tx in expenses)
        {
            var idx = tx.TransactionDate.Day - 1;
            if (idx >= 0 && idx < daysElapsed)
                arr[idx] += tx.Amount.Amount;
        }
        return arr;
    }

    /// <summary>
    /// Exponential Weighted Moving Average.
    /// ewma[i] = α × y[i] + (1−α) × ewma[i−1]
    /// α=0.25 → yaqin kunlar og'irroq, lekin eski kunlar ham hisobga olinadi.
    /// </summary>
    private static decimal ComputeEwma(decimal[] daily, decimal alpha)
    {
        if (daily.Length == 0) return 0;
        var ewma = daily[0];
        for (int i = 1; i < daily.Length; i++)
            ewma = alpha * daily[i] + (1 - alpha) * ewma;
        return ewma;
    }

    /// <summary>
    /// Ordinary Least Squares: y = intercept + slope × x
    /// slope > 0 → xarajat oshib borayapti (Worsening trend)
    /// slope &lt; 0 → xarajat kamayib borayapti (Improving trend)
    /// </summary>
    private static (decimal slope, decimal intercept) ComputeOlsRegression(decimal[] y)
    {
        int n = y.Length;
        if (n < 2) return (0, n == 1 ? y[0] : 0);

        decimal sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX  += i;
            sumY  += y[i];
            sumXY += i * y[i];
            sumX2 += (decimal)i * i;
        }

        var denom = n * sumX2 - sumX * sumX;
        if (denom == 0) return (0, sumY / n);

        var slope     = (n * sumXY - sumX * sumY) / denom;
        var intercept = (sumY - slope * sumX) / n;
        return (slope, intercept);
    }

    /// <summary>
    /// Regression modelini qolgan kunlarga qo'llab, umumiy summa qaytaradi.
    /// Manfiy qiymatlar 0 bilan almashtiriladi (xarajat manfiy bo'lmaydi).
    /// </summary>
    private static decimal PredictRemainingByRegression(
        decimal slope, decimal intercept, int daysElapsed, int daysInMonth)
    {
        decimal sum = 0;
        for (int d = daysElapsed; d < daysInMonth; d++)
            sum += Math.Max(0, intercept + slope * d);
        return sum;
    }

    /// <summary>
    /// Dispersiya (variance) — kunlik sarflashning beqarorligi.
    /// Kichik dispersiya = yuqori ishonch.
    /// </summary>
    private static decimal ComputeVariance(decimal[] daily)
    {
        if (daily.Length < 2) return 0;
        var mean = daily.Sum() / daily.Length;
        return daily.Sum(d => (d - mean) * (d - mean)) / daily.Length;
    }

    /// <summary>
    /// Ishonch foizi 0–95 (hech qachon 100% emas — har doim noaniqlik bor).
    /// Hisob:
    ///   • O'tgan kunlar soni     → max 50 ball
    ///   • Tarixiy ma'lumot bor   → +30 ball
    ///   • Sarflash izchilligi    → max 20 ball (variation koeffitsienti asosida)
    /// </summary>
    private static int ComputeConfidencePct(
        int daysElapsed, decimal lastMonthTotal, decimal stdDev, decimal ewmaRate)
    {
        var score = daysElapsed switch
        {
            >= 20 => 50,
            >= 14 => 40,
            >= 7  => 28,
            >= 3  => 14,
            _     =>  5
        };

        if (lastMonthTotal > 0) score += 30;

        if (ewmaRate > 0)
        {
            var cv = stdDev / ewmaRate; // variation koeffitsienti
            score += cv switch
            {
                < 0.25m => 20,
                < 0.50m => 12,
                < 0.80m =>  6,
                _       =>  0
            };
        }

        return Math.Min(95, score);
    }

    /// <summary>
    /// OLS slope va EWMA asosida trend yo'nalishi.
    /// Nisbiy slope 8% dan yuqori → Worsening; pastda → Improving.
    /// </summary>
    private static SpendingTrend ComputeTrajectory(decimal slope, decimal ewmaRate)
    {
        if (ewmaRate == 0) return SpendingTrend.Stable;
        var relSlope = slope / ewmaRate;
        return relSlope >  0.08m ? SpendingTrend.Worsening
             : relSlope < -0.08m ? SpendingTrend.Improving
             : SpendingTrend.Stable;
    }

    /// <summary>
    /// Har bir kategoriya uchun EWMA prognoz va trend (so'nggi yarmi vs avvalgi yarmi).
    /// </summary>
    private static IReadOnlyList<CategoryPredictionDto> BuildCategoryPredictions(
        List<Domain.Entities.Transaction> expenses,
        int daysElapsed,
        int daysInMonth)
    {
        if (daysElapsed == 0) return Array.Empty<CategoryPredictionDto>();

        var remaining = daysInMonth - daysElapsed;

        return expenses
            .GroupBy(t => t.Category)
            .Select(g =>
            {
                var catTotal = g.Sum(t => t.Amount.Amount);
                var catDaily = new decimal[daysElapsed];
                foreach (var tx in g)
                {
                    var idx = tx.TransactionDate.Day - 1;
                    if (idx >= 0 && idx < daysElapsed)
                        catDaily[idx] += tx.Amount.Amount;
                }

                var ewma      = ComputeEwma(catDaily, 0.25m);
                var predicted = catTotal + ewma * remaining;

                // Trend: so'nggi yarmi vs birinchi yarmi
                var mid       = Math.Max(1, daysElapsed / 2);
                var recentAvg = catDaily.Skip(mid).DefaultIfEmpty(0).Average();
                var olderAvg  = catDaily.Take(mid).DefaultIfEmpty(0).Average();
                var trend     = recentAvg > olderAvg * 1.15m ? SpendingTrend.Worsening
                              : recentAvg < olderAvg * 0.85m ? SpendingTrend.Improving
                              : SpendingTrend.Stable;

                return new CategoryPredictionDto(
                    g.Key,
                    GetCategoryDisplayName(g.Key),
                    Math.Round(catTotal, 0),
                    Math.Round(predicted, 0),
                    trend);
            })
            .OrderByDescending(c => c.PredictedTotal)
            .ToList();
    }

    /// <summary>
    /// O'tgan oy ma'lumotlarini oladi:
    ///   lastMonthTotal    = o'tgan oyning umumiy xarajati
    ///   lastMonthAtSameDay = o'tgan oy xuddi shu kuniga qadar xarajat
    /// </summary>
    private async Task<(decimal lastMonthTotal, decimal lastMonthAtSameDay)> GetLastMonthComparisonAsync(
        Guid userId, int year, int month, int sameDay, CancellationToken ct)
    {
        try
        {
            var prevDate  = new DateOnly(year, month, 1).AddMonths(-1);
            var prevYear  = prevDate.Year;
            var prevMonth = prevDate.Month;

            var (_, prevTotal) = await _txRepo.GetMonthlyTotalsAsync(userId, prevYear, prevMonth, ct);
            if (prevTotal == 0) return (0, 0);

            var prevStart   = new DateOnly(prevYear, prevMonth, 1);
            var prevSameDay = new DateOnly(prevYear, prevMonth,
                Math.Min(sameDay, DateTime.DaysInMonth(prevYear, prevMonth)));

            var prevTxs = await _txRepo.GetByUserIdAndDateRangeAsync(userId, prevStart, prevSameDay, ct);
            var prevAtSameDay = prevTxs
                .Where(t => t.Type == TransactionType.Expense)
                .Sum(t => t.Amount.Amount);

            return (prevTotal, prevAtSameDay);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load last-month data for user {UserId}", userId);
            return (0, 0); // graceful degradation
        }
    }

    private static string BuildEnhancedWarning(
        decimal predicted, decimal income, decimal pacePct, SpendingTrend trajectory)
    {
        if (income <= 0) return string.Empty;

        var parts = new List<string>(2);
        var ratio = predicted / income;

        if      (ratio > 1.20m) parts.Add("Xarajatlar daromaddan 20%+ oshishi kutilmoqda! 🚨");
        else if (ratio > 1.00m) parts.Add("Xarajatlar daromaddan oshib ketishi taxmin qilinmoqda ⚠️");
        else if (ratio > 0.90m) parts.Add("Xarajatlar daromadga yaqinlashmoqda, ehtiyot bo'ling ⚠️");

        if (pacePct > 125m && trajectory == SpendingTrend.Worsening)
            parts.Add("Sarflash sur'ati tezlashmoqda!");

        return string.Join(" | ", parts);
    }

    private static SpendingPredictionDto BuildPastMonthResult(
        Guid userId, int year, int month, int daysInMonth,
        decimal income, decimal expenses)
    {
        var dailyAvg     = daysInMonth > 0 ? Math.Round(expenses / daysInMonth, 0) : 0;
        var savings      = income - expenses;
        var savingsRate  = income > 0 ? Math.Round(savings / income * 100, 1) : 0m;

        return new SpendingPredictionDto(
            userId, year, month,
            Math.Round(expenses, 0),
            Math.Round(income, 0),
            daysInMonth, daysInMonth,
            Math.Round(expenses, 0),
            Math.Round(expenses, 0),
            Math.Round(expenses, 0),
            Math.Round(savings, 0),
            savingsRate,
            dailyAvg, 0, 100m,
            SpendingTrend.Stable, 0,
            null, null,
            PredictionConfidence.High, 95,
            Array.Empty<CategoryPredictionDto>(),
            "Bu oy tugagan — haqiqiy ma'lumotlar ko'rsatilmoqda.");
    }

    private static SpendingPredictionDto BuildNoDataResult(
        Guid userId, int year, int month,
        decimal income, int daysInMonth, int daysElapsed)
    {
        var dailyBudget = income > 0 && daysInMonth > 0
            ? Math.Round(income / daysInMonth, 0)
            : 0;

        return new SpendingPredictionDto(
            userId, year, month,
            0, Math.Round(income, 0),
            daysElapsed, daysInMonth,
            0, 0, 0,
            Math.Round(income, 0), 100m,
            0, dailyBudget, 0m,
            SpendingTrend.Stable, 0,
            null, null,
            PredictionConfidence.Low, 5,
            Array.Empty<CategoryPredictionDto>(),
            string.Empty);
    }

    // ── General analytics helpers ─────────────────────────────────────────

    private static AnalyticsReportDto BuildReport(
        Guid userId,
        IEnumerable<Domain.Entities.Transaction> transactions,
        ReportPeriod period,
        DateOnly from,
        DateOnly to)
    {
        var txList        = transactions.ToList();
        var totalExpenses = txList.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount.Amount);
        var totalIncome   = txList.Where(t => t.Type == TransactionType.Income) .Sum(t => t.Amount.Amount);

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
                    totalExpenses > 0 ? Math.Round(amount / totalExpenses * 100, 1) : 0,
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
                var actual     = categoryAmounts.GetValueOrDefault(category, 0);
                var actualPct  = totalIncome > 0
                    ? Math.Round(actual / totalIncome * 100, 1)
                    : 0m;

                var status = actualPct switch
                {
                    var p when p <= recommended * 0.9m  => RatioStatus.UnderBudget,
                    var p when p <= recommended * 1.1m  => RatioStatus.OnTrack,
                    var p when p <= recommended * 1.3m  => RatioStatus.SlightlyOver,
                    _                                   => RatioStatus.SignificantlyOver
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

        foreach (var (category, amount) in categoryAmounts)
        {
            if (!IsExpenseCategory(category)) continue;
            if (!RecommendedRatios.TryGetValue(category, out var recommended)) continue;
            if (totalIncome <= 0) continue;

            var pct = amount / totalIncome * 100;
            if (pct > recommended * 1.5m)
                warnings.Add($"{GetCategoryDisplayName(category)} xarajatlari tavsiyadan {pct / recommended:F1}x ko'p.");
        }

        return warnings;
    }

    private static List<string> BuildStrengths(decimal savingsRate)
    {
        var strengths = new List<string>();
        if      (savingsRate >= 20) strengths.Add($"Ajoyib! Daromadingizning {savingsRate:F1}% ni jamg'ardingiz.");
        else if (savingsRate >= 10) strengths.Add($"Yaxshi! {savingsRate:F1}% jamg'arma qilindingiz.");
        return strengths;
    }

    private static HealthScore CalculateHealthScore(
        decimal totalIncome, decimal totalExpenses, decimal savingsRate, int warningCount)
    {
        if (totalIncome == 0)
            return totalExpenses > 0 ? HealthScore.Critical : HealthScore.Fair;

        var score = 100;
        score -= warningCount * 15;

        if      (savingsRate <  0) score -= 30;
        else if (savingsRate <  5) score -= 20;
        else if (savingsRate < 10) score -= 10;
        else if (savingsRate >= 20) score += 10;

        score = Math.Clamp(score, 0, 110);

        return score switch
        {
            >= 90 => HealthScore.Excellent,
            >= 70 => HealthScore.Good,
            >= 50 => HealthScore.Fair,
            >= 30 => HealthScore.Poor,
            _     => HealthScore.Critical
        };
    }

    private static bool IsExpenseCategory(TransactionCategory category) =>
        category is not (TransactionCategory.Salary    or TransactionCategory.Freelance
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
        _                                 => category.ToString()
    };
}
