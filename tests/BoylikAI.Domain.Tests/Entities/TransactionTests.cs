using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Events;
using FluentAssertions;
using Xunit;

namespace BoylikAI.Domain.Tests.Entities;

public sealed class TransactionTests
{
    [Fact]
    public void Create_ValidParameters_CreatesTransactionWithDomainEvent()
    {
        // Arrange
        var userId = Guid.NewGuid();

        // Act
        var tx = Transaction.Create(
            userId: userId,
            type: TransactionType.Expense,
            amount: 35000,
            currency: "UZS",
            category: TransactionCategory.Food,
            description: "Lunch at cafe",
            transactionDate: DateOnly.FromDateTime(DateTime.Today),
            originalMessage: "Kafeda 35 ming ishlatdim",
            isAiParsed: true,
            aiConfidenceScore: 0.92m);

        // Assert
        tx.UserId.Should().Be(userId);
        tx.Amount.Amount.Should().Be(35000);
        tx.Amount.Currency.Should().Be("UZS");
        tx.Category.Should().Be(TransactionCategory.Food);
        tx.Type.Should().Be(TransactionType.Expense);
        tx.IsAiParsed.Should().BeTrue();
        tx.AiConfidenceScore.Should().Be(0.92m);
        tx.Id.Should().NotBeEmpty();

        tx.DomainEvents.Should().HaveCount(1);
        tx.DomainEvents.First().Should().BeOfType<TransactionCreatedEvent>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Create_ZeroOrNegativeAmount_ThrowsArgumentException(decimal amount)
    {
        // Act & Assert
        var act = () => Transaction.Create(
            Guid.NewGuid(), TransactionType.Expense,
            amount, "UZS", TransactionCategory.Other,
            "test", DateOnly.FromDateTime(DateTime.Today));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*amount*");
    }

    [Fact]
    public void Create_IncomeTransaction_HasCorrectType()
    {
        var tx = Transaction.Create(
            Guid.NewGuid(), TransactionType.Income,
            5_000_000, "UZS", TransactionCategory.Salary,
            "Monthly salary", DateOnly.FromDateTime(DateTime.Today));

        tx.Type.Should().Be(TransactionType.Income);
        tx.Category.Should().Be(TransactionCategory.Salary);
    }
}
