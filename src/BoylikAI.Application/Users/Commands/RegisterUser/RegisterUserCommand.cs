using BoylikAI.Application.DTOs;
using MediatR;

namespace BoylikAI.Application.Users.Commands.RegisterUser;

public sealed record RegisterUserCommand(
    long TelegramId,
    string? Username,
    string? FirstName,
    string? LastName,
    string LanguageCode = "uz") : IRequest<RegisterUserResult>;

public sealed record RegisterUserResult(
    bool IsNewUser,
    UserDto User);
