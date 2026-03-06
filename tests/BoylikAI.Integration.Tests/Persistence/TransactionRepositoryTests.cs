using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Infrastructure.Persistence;
using BoylikAI.Infrastructure.Persistence.Repositories;
using BoylikAI.Integration.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BoylikAI.Integration.Tests.Persistence;

[Collection("Integration")]
public sealed class TransactionRepositoryTests : IAsyncLifetime
{
    private readonly IntegrationFixture _fixture;
    private ApplicationDbContext _db = null!;
    private TransactionRepository _repo = null!;

    public TransactionRepositoryTests(IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
        var scope = _fixture.Services.CreateScope();
        _db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        _repo = new TransactionRepository(_db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_ThenGetById_ReturnsCorrectTransaction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);
        var tx = Transaction.Create(userId, TransactionType.Expense, 35_000, "UZS",
            TransactionCategory.Food, "Lunch", DateOnly.FromDateTime(DateTime.UtcNow));

        // Act
        await _repo.AddAsync(tx);
        await _db.SaveChangesAsync();

        var loaded = await _repo.GetByIdAsync(tx.Id);

        // Assert
        loaded.Should().NotBeNull();
        loaded!.Amount.Amount.Should().Be(35_000);
        loaded.Category.Should().Be(TransactionCategory.Food);
        loaded.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteAsync_MarksDeletedAndHidesFromQueries()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);
        var tx = Transaction.Create(userId, TransactionType.Expense, 20_000, "UZS",
            TransactionCategory.Transport, "Bus", DateOnly.FromDateTime(DateTime.UtcNow));

        await _repo.AddAsync(tx);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        await _repo.SoftDeleteAsync(tx.Id);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Assert — global query filter excludes deleted records
        var result = await _repo.GetByUserIdAsync(userId);
        result.Should().BeEmpty();

        // But we can still find it with IgnoreQueryFilters
        var raw = await _db.Transactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == tx.Id);
        raw.Should().NotBeNull();
        raw!.IsDeleted.Should().BeTrue();
        raw.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetMonthlyTotalsAsync_ReturnsCorrectIncomeAndExpenses()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);

        var month = new DateOnly(2025, 3, 1);
        await AddTransactionAsync(userId, TransactionType.Income, 5_000_000, TransactionCategory.Salary, month);
        await AddTransactionAsync(userId, TransactionType.Expense, 500_000, TransactionCategory.Food, month);
        await AddTransactionAsync(userId, TransactionType.Expense, 200_000, TransactionCategory.Transport, month);
        await _db.SaveChangesAsync();

        // Act
        var (income, expenses) = await _repo.GetMonthlyTotalsAsync(userId, 2025, 3);

        // Assert
        income.Should().Be(5_000_000);
        expenses.Should().Be(700_000);
    }

    [Fact]
    public async Task GetMonthlyCategorySummaryAsync_GroupsCorrectly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        await SeedUserAsync(userId);

        var month = new DateOnly(2025, 3, 1);
        await AddTransactionAsync(userId, TransactionType.Expense, 50_000, TransactionCategory.Food, month);
        await AddTransactionAsync(userId, TransactionType.Expense, 30_000, TransactionCategory.Food, month);
        await AddTransactionAsync(userId, TransactionType.Expense, 20_000, TransactionCategory.Transport, month);
        await _db.SaveChangesAsync();

        // Act
        var summary = await _repo.GetMonthlyCategorySummaryAsync(
            userId, 2025, 3, TransactionType.Expense);

        // Assert
        summary.Should().HaveCount(2);

        var food = summary.First(s => s.Category == TransactionCategory.Food);
        food.TotalAmount.Should().Be(80_000);
        food.Count.Should().Be(2);

        var transport = summary.First(s => s.Category == TransactionCategory.Transport);
        transport.TotalAmount.Should().Be(20_000);
    }

    [Fact]
    public async Task GetByUserIdAndMonthAsync_DoesNotReturnOtherUsersData()
    {
        // Arrange
        var userId1 = Guid.NewGuid();
        var userId2 = Guid.NewGuid();
        await SeedUserAsync(userId1);
        await SeedUserAsync(userId2);

        var month = new DateOnly(2025, 3, 1);
        await AddTransactionAsync(userId1, TransactionType.Expense, 100_000, TransactionCategory.Food, month);
        await AddTransactionAsync(userId2, TransactionType.Expense, 200_000, TransactionCategory.Food, month);
        await _db.SaveChangesAsync();

        // Act
        var result = await _repo.GetByUserIdAndMonthAsync(userId1, 2025, 3);

        // Assert
        result.Should().HaveCount(1);
        result[0].UserId.Should().Be(userId1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task SeedUserAsync(Guid userId)
    {
        // Insert a minimal user row to satisfy FK constraint
        await _db.Database.ExecuteSqlRawAsync(
            """INSERT INTO users (id, telegram_id, username, first_name, last_name, language_code, is_notifications_enabled, created_at, last_activity_at)
               VALUES ({0}, {1}, 'testuser', 'Test', 'User', 'uz', true, NOW(), NOW())""",
            userId, (long)Random.Shared.NextInt64(1, long.MaxValue));
    }

    private async Task AddTransactionAsync(
        Guid userId, TransactionType type, decimal amount,
        TransactionCategory category, DateOnly date)
    {
        var tx = Transaction.Create(userId, type, amount, "UZS", category, "test", date);
        await _repo.AddAsync(tx);
    }
}
