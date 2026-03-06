using BoylikAI.Application.DTOs;
using BoylikAI.Application.Transactions.Commands.CreateTransaction;
using BoylikAI.Application.Transactions.Queries.GetTransactions;
using BoylikAI.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BoylikAI.API.Controllers;

[ApiController]
[Route("api/v1/users/{userId:guid}/transactions")]
[Produces("application/json")]
[Authorize]
public sealed class TransactionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public TransactionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Returns paginated transactions for a user.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TransactionDto>), 200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetTransactions(
        Guid userId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] TransactionType? type,
        [FromQuery] TransactionCategory? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!IsAuthorizedForUser(userId)) return Forbid();

        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest("page must be >= 1 and pageSize must be 1–100");

        var result = await _mediator.Send(
            new GetTransactionsQuery(userId, from, to, type, category, page, pageSize), ct);
        return Ok(result);
    }

    /// <summary>Manually creates a transaction (no NLP parsing).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(TransactionDto), 201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> CreateTransaction(
        Guid userId,
        [FromBody] CreateTransactionRequest request,
        CancellationToken ct = default)
    {
        if (!IsAuthorizedForUser(userId)) return Forbid();

        if (request.Amount <= 0)
            return BadRequest("Amount must be positive");

        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest("Description is required");

        var dto = await _mediator.Send(new CreateTransactionCommand(
            userId,
            request.Type,
            request.Amount,
            request.Currency ?? "UZS",
            request.Category,
            request.Description,
            request.TransactionDate ?? DateOnly.FromDateTime(DateTime.UtcNow)), ct);

        return CreatedAtAction(nameof(GetTransactions), new { userId }, dto);
    }

    // Ensures the authenticated user can only access their own data
    private bool IsAuthorizedForUser(Guid userId)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return sub is not null && sub == userId.ToString();
    }
}

public sealed record CreateTransactionRequest(
    TransactionType Type,
    decimal Amount,
    string? Currency,
    TransactionCategory Category,
    string Description,
    DateOnly? TransactionDate);
