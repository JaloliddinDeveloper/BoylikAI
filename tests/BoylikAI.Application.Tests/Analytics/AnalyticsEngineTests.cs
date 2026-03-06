using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Interfaces;
using BoylikAI.Infrastructure.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BoylikAI.Application.Tests.Analytics;

public sealed class AnalyticsEngineTests
{
    private readonly Mock<ITransactionRepository> _txRepoMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    private AnalyticsEngine CreateEngine() =>
        new(_txRepoMock.Object, NullLogger<AnalyticsEngine>.Instance);

    // ── GetMonthlyReport ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMonthlyReport_WithExpenses_CalculatesCorrectCategoryBreakdown()
    {
        // Arrange
        var transactions = new List<Transaction>
        {
            MakeExpense(50_000, TransactionCategory.Food),
            MakeExpense(30_000, TransactionCategory.Food),
            MakeExpense(20_000, TransactionCategory.Transport),
            MakeIncome(200_000, TransactionCategory.Salary)
        };

        _txRepoMock.Setup(r => r.GetByUserIdAndDateRangeAsync(
                _userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        var engine = CreateEngine();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Act
        var report = await engine.GetDailyReportAsync(_userId, today);

        // Assert
        report.TotalIncome.Should().Be(200_000);
        report.TotalExpenses.Should().Be(100_000);
        report.NetBalance.Should().Be(100_000);
        report.CategoryBreakdown.Should().HaveCount(2);

        var foodBreakdown = report.CategoryBreakdown.First(c => c.Category == TransactionCategory.Food);
        foodBreakdown.Amount.Should().Be(80_000);
        foodBreakdown.Percentage.Should().Be(80.0m);

        var transportBreakdown = report.CategoryBreakdown.First(c => c.Category == TransactionCategory.Transport);
        transportBreakdown.Amount.Should().Be(20_000);
        transportBreakdown.Percentage.Should().Be(20.0m);
    }

    [Fact]
    public async Task GetMonthlyReport_NoTransactions_ReturnsZeroTotals()
    {
        _txRepoMock.Setup(r => r.GetByUserIdAndDateRangeAsync(
                _userId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var engine = CreateEngine();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var report = await engine.GetDailyReportAsync(_userId, today);

        report.TotalIncome.Should().Be(0);
        report.TotalExpenses.Should().Be(0);
        report.NetBalance.Should().Be(0);
        report.CategoryBreakdown.Should().BeEmpty();
    }

    // ── FinancialHealth ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetFinancialHealth_HighSavingsRate_ReturnsExcellentScore()
    {
        SetupMonthlyTotals(income: 10_000_000, expenses: 7_000_000);
        SetupCategorySummary([]);

        var engine = CreateEngine();
        var result = await engine.GetFinancialHealthAsync(_userId, 2025, 1);

        result.SavingsRate.Should().Be(30.0m);
        result.OverallScore.Should().Be(HealthScore.Excellent);
    }

    [Fact]
    public async Task GetFinancialHealth_ExpensesExceedIncome_ReturnsCriticalScore()
    {
        SetupMonthlyTotals(income: 3_000_000, expenses: 5_000_000);
        SetupCategorySummary([]);

        var engine = CreateEngine();
        var result = await engine.GetFinancialHealthAsync(_userId, 2025, 1);

        result.Warnings.Should().Contain(w => w.Contains("daromaddan"));
        result.SavingsAmount.Should().BeNegative();
        result.OverallScore.Should().BeOneOf(HealthScore.Poor, HealthScore.Critical);
    }

    [Fact]
    public async Task GetFinancialHealth_ZeroIncome_WithExpenses_ReturnsCriticalScore()
    {
        SetupMonthlyTotals(income: 0, expenses: 500_000);
        SetupCategorySummary([]);

        var engine = CreateEngine();
        var result = await engine.GetFinancialHealthAsync(_userId, 2025, 1);

        result.OverallScore.Should().Be(HealthScore.Critical);
    }

    [Fact]
    public async Task GetFinancialHealth_ZeroIncomeZeroExpenses_ReturnsFairScore()
    {
        SetupMonthlyTotals(income: 0, expenses: 0);
        SetupCategorySummary([]);

        var engine = CreateEngine();
        var result = await engine.GetFinancialHealthAsync(_userId, 2025, 1);

        // No activity — not critical, just fair
        result.OverallScore.Should().Be(HealthScore.Fair);
    }

    [Fact]
    public async Task GetFinancialHealth_FoodOverBudget_GeneratesWarning()
    {
        SetupMonthlyTotals(income: 5_000_000, expenses: 3_000_000);
        // Food at 50% of income — recommended is 25%, threshold is 25*1.5=37.5%
        SetupCategorySummary([
            new CategorySummary(TransactionCategory.Food, 2_500_000, 30)
        ]);

        var engine = CreateEngine();
        var result = await engine.GetFinancialHealthAsync(_userId, 2025, 1);

        result.Warnings.Should().Contain(w => w.Contains("Oziq-ovqat") || w.Contains("Food"));
    }

    [Fact]
    public async Task GetFinancialHealth_SalaryInWarnings_ShouldBeIgnored()
    {
        // Bug regression: income categories must NOT trigger warnings
        SetupMonthlyTotals(income: 5_000_000, expenses: 2_000_000);
        SetupCategorySummary([
            new CategorySummary(TransactionCategory.Salary, 5_000_000, 1) // 100% of income — should not warn
        ]);

        var engine = CreateEngine();
        var result = await engine.GetFinancialHealthAsync(_userId, 2025, 1);

        // Salary is income — must not appear in warnings
        result.Warnings.Should().NotContain(w => w.Contains("Oylik") || w.Contains("Salary"));
    }

    // ── SpendingPrediction ───────────────────────────────────────────────────

    [Fact]
    public async Task PredictMonthEnd_PastMonth_ReturnsActualData()
    {
        // Past month — no prediction needed, actual data returned
        SetupMonthlyTotals(income: 5_000_000, expenses: 3_000_000);

        var engine = CreateEngine();
        var result = await engine.PredictMonthEndSpendingAsync(_userId, 2024, 1);

        result.CurrentSpending.Should().Be(result.PredictedTotal);
        result.Confidence.Should().Be(PredictionConfidence.High);
        result.Warning.Should().Contain("haqiqiy");
    }

    [Fact]
    public async Task PredictMonthEnd_EarlyInMonth_LowConfidence()
    {
        var now = DateTime.UtcNow;
        if (now.Day > 3)
        {
            // Can only test this on days 1-3
            return;
        }

        SetupMonthlyTotals(income: 5_000_000, expenses: 500_000);
        SetupMonthTransactions([MakeExpense(500_000, TransactionCategory.Food)]);

        var engine = CreateEngine();
        var result = await engine.PredictMonthEndSpendingAsync(_userId, now.Year, now.Month);

        result.Confidence.Should().Be(PredictionConfidence.Low);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetupMonthlyTotals(decimal income, decimal expenses)
    {
        _txRepoMock.Setup(r => r.GetMonthlyTotalsAsync(
                _userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((income, expenses));
    }

    private void SetupCategorySummary(IReadOnlyList<CategorySummary> data)
    {
        _txRepoMock.Setup(r => r.GetMonthlyCategorySummaryAsync(
                _userId, It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<TransactionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);
    }

    private void SetupMonthTransactions(List<Transaction> transactions)
    {
        _txRepoMock.Setup(r => r.GetByUserIdAndMonthAsync(
                _userId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);
    }

    private static Transaction MakeExpense(decimal amount, TransactionCategory category) =>
        Transaction.Create(Guid.NewGuid(), TransactionType.Expense, amount, "UZS",
            category, "test", DateOnly.FromDateTime(DateTime.UtcNow));

    private static Transaction MakeIncome(decimal amount, TransactionCategory category) =>
        Transaction.Create(Guid.NewGuid(), TransactionType.Income, amount, "UZS",
            category, "test", DateOnly.FromDateTime(DateTime.UtcNow));
}
