using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Application.DTOs;
using BoylikAI.Domain.Entities;
using BoylikAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BoylikAI.Application.Users.Commands.RegisterUser;

public sealed class RegisterUserCommandHandler
    : IRequestHandler<RegisterUserCommand, RegisterUserResult>
{
    private readonly IUserRepository _userRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly ILogger<RegisterUserCommandHandler> _logger;

    public RegisterUserCommandHandler(
        IUserRepository userRepo,
        IUnitOfWork unitOfWork,
        ICacheService cache,
        ILogger<RegisterUserCommandHandler> logger)
    {
        _userRepo = userRepo;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task<RegisterUserResult> Handle(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var existing = await _userRepo.GetByTelegramIdAsync(request.TelegramId, cancellationToken);
        if (existing is not null)
        {
            existing.UpdateLastActivity();
            await _userRepo.UpdateAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new RegisterUserResult(false, MapToDto(existing));
        }

        var user = User.Create(
            request.TelegramId,
            request.Username,
            request.FirstName,
            request.LastName,
            request.LanguageCode);

        await _userRepo.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Cache the new user lookup
        await _cache.SetAsync(
            $"user:telegram:{request.TelegramId}",
            MapToDto(user),
            TimeSpan.FromHours(1),
            cancellationToken);

        _logger.LogInformation("New user registered: TelegramId={TelegramId}, UserId={UserId}",
            request.TelegramId, user.Id);

        return new RegisterUserResult(true, MapToDto(user));
    }

    private static UserDto MapToDto(User u) => new(
        u.Id, u.TelegramId, u.Username, u.FirstName, u.LastName,
        u.LanguageCode, u.DefaultCurrency, u.MonthlyBudgetLimit,
        u.IsNotificationsEnabled, u.CreatedAt, u.LastActivityAt);
}
