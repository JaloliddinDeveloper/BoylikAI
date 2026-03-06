using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using BoylikAI.Application.Transactions.Commands.ParseAndCreate;
using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Enums;
using BoylikAI.Domain.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BoylikAI.Application.Tests.Transactions;

public sealed class ParseAndCreateTransactionCommandHandlerTests
{
    private readonly Mock<ITransactionParser> _parserMock = new();
    private readonly Mock<ITransactionRepository> _txRepoMock = new();
    private readonly Mock<IUserRepository> _userRepoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ICacheService> _cacheMock = new();
    private readonly Mock<ILogger<ParseAndCreateTransactionCommandHandler>> _loggerMock = new();

    private ParseAndCreateTransactionCommandHandler CreateHandler() =>
        new(_parserMock.Object, _txRepoMock.Object, _userRepoMock.Object,
            _uowMock.Object, _cacheMock.Object, _loggerMock.Object);

    [Fact]
    public async Task Handle_ValidExpenseMessage_CreatesTransactionSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create(12345L, "testuser", "Test", "User");

        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _parserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedTransactionDto(
                TransactionType.Expense,
                Amount: 2400,
                Currency: "UZS",
                Category: TransactionCategory.Transport,
                Description: "bus fare",
                Date: DateOnly.FromDateTime(DateTime.Today),
                ConfidenceScore: 0.95m,
                OriginalMessage: "Avtobusga 2400 so'm berdim"));

        _cacheMock.Setup(c => c.RemoveByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new ParseAndCreateTransactionCommand(
            userId, 12345L, "Avtobusga 2400 so'm berdim", "uz");

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Transaction.Should().NotBeNull();
        result.Transaction!.Amount.Should().Be(2400);
        result.Transaction.Category.Should().Be(TransactionCategory.Transport);
        result.Transaction.Type.Should().Be(TransactionType.Expense);
        result.IsAmbiguous.Should().BeFalse();

        _txRepoMock.Verify(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Once);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_LowConfidenceParse_ReturnsAmbiguousResult()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create(12345L, "testuser", "Test", "User");

        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _parserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ParsedTransactionDto(
                TransactionType.Expense,
                Amount: 5000,
                Currency: "UZS",
                Category: TransactionCategory.Other,
                Description: "unknown",
                Date: DateOnly.FromDateTime(DateTime.Today),
                ConfidenceScore: 0.45m));  // Below threshold

        var command = new ParseAndCreateTransactionCommand(userId, 12345L, "5000", "uz");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.IsAmbiguous.Should().BeTrue();
        result.ClarificationQuestion.Should().NotBeNullOrEmpty();

        _txRepoMock.Verify(r => r.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NonFinancialMessage_ReturnsFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var user = User.Create(12345L, "testuser", "Test", "User");

        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        _parserMock.Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParsedTransactionDto?)null);

        var command = new ParseAndCreateTransactionCommand(userId, 12345L, "Salom, qalaysiz?", "uz");
        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(command, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
        result.IsAmbiguous.Should().BeFalse();
    }
}
