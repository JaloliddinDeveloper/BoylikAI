using BoylikAI.Application.Analytics.Queries.GetMonthlyReport;
using BoylikAI.Application.DTOs;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BoylikAI.API.Controllers;

[ApiController]
[Route("api/v1/users/{userId:guid}/analytics")]
[Produces("application/json")]
[Authorize]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Monthly expense report with category breakdown.</summary>
    [HttpGet("monthly")]
    [ProducesResponseType(typeof(AnalyticsReportDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetMonthlyReport(
        Guid userId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct = default)
    {
        if (!IsAuthorizedForUser(userId)) return Forbid();
        var now = DateTime.UtcNow;
        var result = await _mediator.Send(
            new GetMonthlyReportQuery(userId, year ?? now.Year, month ?? now.Month), ct);
        return Ok(result);
    }

    /// <summary>Financial health score and category ratio analysis.</summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(FinancialHealthDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetFinancialHealth(
        Guid userId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct = default)
    {
        if (!IsAuthorizedForUser(userId)) return Forbid();
        var now = DateTime.UtcNow;
        var result = await _mediator.Send(
            new GetFinancialHealthQuery(userId, year ?? now.Year, month ?? now.Month), ct);
        return Ok(result);
    }

    /// <summary>Month-end spending prediction based on current trends.</summary>
    [HttpGet("prediction")]
    [ProducesResponseType(typeof(SpendingPredictionDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetSpendingPrediction(
        Guid userId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct = default)
    {
        if (!IsAuthorizedForUser(userId)) return Forbid();
        var now = DateTime.UtcNow;
        var result = await _mediator.Send(
            new GetSpendingPredictionQuery(userId, year ?? now.Year, month ?? now.Month), ct);
        return Ok(result);
    }

    /// <summary>AI-generated personalized financial advice.</summary>
    [HttpGet("advice")]
    [ProducesResponseType(typeof(FinancialAdviceDto), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetFinancialAdvice(
        Guid userId,
        [FromQuery] int? year,
        [FromQuery] int? month,
        [FromQuery] string lang = "uz",
        CancellationToken ct = default)
    {
        if (!IsAuthorizedForUser(userId)) return Forbid();
        var now = DateTime.UtcNow;
        var result = await _mediator.Send(
            new GetFinancialAdviceQuery(userId, year ?? now.Year, month ?? now.Month, lang), ct);
        return Ok(result);
    }

    private bool IsAuthorizedForUser(Guid userId)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return sub is not null && sub == userId.ToString();
    }
}
