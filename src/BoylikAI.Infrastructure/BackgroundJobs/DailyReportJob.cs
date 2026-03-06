using BoylikAI.Application.Analytics.Queries.GetMonthlyReport;
using BoylikAI.Application.Common.Interfaces;
using BoylikAI.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace BoylikAI.Infrastructure.BackgroundJobs;

/// <summary>
/// Hangfire recurring job that sends daily financial summaries to active users.
/// Scheduled: every day at 21:00 Tashkent time (UTC+5 = 16:00 UTC).
/// </summary>
public sealed class DailyReportJob
{
    private readonly IUserRepository _userRepo;
    private readonly IMediator _mediator;
    private readonly INotificationService _notificationService;
    private readonly ILogger<DailyReportJob> _logger;

    // Telegram global limit: 30 msg/sec. 50 concurrent tasks + 20ms delay keeps us safely under.
    private const int MaxConcurrency = 50;
    private const int TelegramDelayMs = 20;

    public DailyReportJob(
        IUserRepository userRepo,
        IMediator mediator,
        INotificationService notificationService,
        ILogger<DailyReportJob> logger)
    {
        _userRepo = userRepo;
        _mediator = mediator;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        _logger.LogInformation("Starting daily report job at {Time}", startTime);

        var now = DateTime.UtcNow;
        var year = now.Year;
        var month = now.Month;

        // Load only IDs + minimal fields — avoids loading all 100K user objects
        var users = await _userRepo.GetActiveUsersAsync(ct);
        var eligible = users.Where(u => u.IsNotificationsEnabled).ToList();

        _logger.LogInformation("Daily report job processing {Count} eligible users", eligible.Count);

        // SemaphoreSlim bounds concurrency — prevents Task.WhenAll() spawning 100K tasks at once
        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = eligible.Select(u => ProcessWithSemaphoreAsync(
            semaphore, u.Id, u.TelegramId, u.LanguageCode, year, month, ct));

        await Task.WhenAll(tasks);

        var elapsed = DateTimeOffset.UtcNow - startTime;
        _logger.LogInformation(
            "Daily report job completed for {Count} users in {Elapsed:hh\\:mm\\:ss}",
            eligible.Count, elapsed);
    }

    private async Task ProcessWithSemaphoreAsync(
        SemaphoreSlim semaphore,
        Guid userId, long telegramId, string languageCode,
        int year, int month, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            await ProcessUserAsync(userId, telegramId, languageCode, year, month, ct);
            // Enforce Telegram rate limit: space out messages across all concurrent workers
            await Task.Delay(TelegramDelayMs, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ProcessUserAsync(
        Guid userId, long telegramId, string languageCode,
        int year, int month, CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var report = await _mediator.Send(new GetMonthlyReportQuery(userId, year, month), ct);

            // Skip users with no activity this month
            if (report.TotalExpenses == 0 && report.TotalIncome == 0) return;

            var message = FormatDailyMessage(report, today, languageCode);
            await _notificationService.SendTextAsync(telegramId, message, ct);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            // Per-user failure must not abort the entire job
            _logger.LogError(ex, "Failed to send daily report to user {UserId}", userId);
        }
    }

    private static string FormatDailyMessage(
        Application.DTOs.AnalyticsReportDto report,
        DateOnly date,
        string lang)
    {
        var top3 = report.CategoryBreakdown.Take(3).ToList();
        var breakdown = top3.Count > 0
            ? string.Join("\n", top3.Select(c => $"  {c.CategoryDisplayName}: {c.Amount:N0}"))
            : (lang == "uz" ? "  Xarajat yo'q" : "  No expenses");

        if (lang == "uz")
        {
            return $"""
                📊 *{date:d MMMM} kunlik hisobot*

                💰 Daromad: {report.TotalIncome:N0} {report.Currency}
                💸 Xarajat: {report.TotalExpenses:N0} {report.Currency}
                📈 Balans: {report.NetBalance:N0} {report.Currency}

                *Asosiy xarajatlar:*
                {breakdown}

                /hisobot \- to'liq oylik hisobot
                """;
        }

        return $"""
            📊 *Daily Summary {date:d MMM}*

            💰 Income: {report.TotalIncome:N0} {report.Currency}
            💸 Expenses: {report.TotalExpenses:N0} {report.Currency}
            📈 Balance: {report.NetBalance:N0} {report.Currency}

            *Top categories:*
            {breakdown}

            /report \- full monthly report
            """;
    }
}
