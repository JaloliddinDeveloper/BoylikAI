namespace BoylikAI.Application.DTOs;

public sealed record UserDto(
    Guid Id,
    long TelegramId,
    string? Username,
    string? FirstName,
    string? LastName,
    string LanguageCode,
    string DefaultCurrency,
    decimal? MonthlyBudgetLimit,
    bool IsNotificationsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt);

public sealed record CreateUserDto(
    long TelegramId,
    string? Username,
    string? FirstName,
    string? LastName,
    string LanguageCode = "uz");
