using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Cards;
using Yanban.Application.Common;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// Cards live under a list for creation/listing, but are addressed directly under the
/// board for get/update/delete. The card version (<c>xmin</c>) is surfaced as a strong
/// <c>ETag</c>; updates require it back as <c>If-Match</c> (optimistic concurrency).
/// </summary>
public class CardsController : BoardScopedController
{
    private readonly ICardService _cards;

    public CardsController(YanbanDbContext db, IAuthorizationService authz, ICardService cards)
        : base(db, authz) => _cards = cards;

    [HttpGet("boards/{boardId:guid}/lists/{listId:guid}/cards")]
    public async Task<ActionResult<IReadOnlyList<CardDto>>> ListCards(Guid boardId, Guid listId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _cards.ListAsync(boardId, listId, ct));
    }

    [HttpPost("boards/{boardId:guid}/lists/{listId:guid}/cards")]
    public async Task<ActionResult<CardDto>> Create(Guid boardId, Guid listId, CreateCardRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var card = await _cards.CreateAsync(boardId, listId, UserId, request, ct);
        SetETag(card.Version);
        return CreatedAtAction(nameof(Get), new { boardId, cardId = card.Id }, card);
    }

    [HttpGet("boards/{boardId:guid}/cards/{cardId:guid}")]
    public async Task<ActionResult<CardDto>> Get(Guid boardId, Guid cardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        var card = await _cards.GetAsync(boardId, cardId, ct);
        SetETag(card.Version);
        return Ok(card);
    }

    [HttpPut("boards/{boardId:guid}/cards/{cardId:guid}")]
    public async Task<ActionResult<CardDto>> Update(Guid boardId, Guid cardId, UpdateCardRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var card = await _cards.UpdateAsync(boardId, cardId, ExpectedVersion(), request, ct);
        SetETag(card.Version);
        return Ok(card);
    }

    [HttpPost("boards/{boardId:guid}/cards/{cardId:guid}/move")]
    public async Task<ActionResult<CardDto>> Move(Guid boardId, Guid cardId, MoveCardRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var card = await _cards.MoveAsync(boardId, cardId, request, ct);
        SetETag(card.Version);
        return Ok(card);
    }

    [HttpPut("boards/{boardId:guid}/cards/{cardId:guid}/assignee")]
    public async Task<ActionResult<CardDto>> Assign(Guid boardId, Guid cardId, AssignCardRequest request, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        var card = await _cards.AssignAsync(boardId, cardId, request.AssigneeId, ct);
        SetETag(card.Version);
        return Ok(card);
    }

    [HttpDelete("boards/{boardId:guid}/cards/{cardId:guid}")]
    public async Task<IActionResult> Delete(Guid boardId, Guid cardId, CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Write, ct);
        await _cards.DeleteAsync(boardId, cardId, ct);
        return NoContent();
    }

    private void SetETag(uint version) => Response.Headers.ETag = $"\"{version}\"";

    /// <summary>Reads the mandatory <c>If-Match</c> header as the card's expected xmin version.</summary>
    private uint ExpectedVersion()
    {
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
            throw new PreconditionRequiredAppException("An If-Match header carrying the card version is required.");

        if (!uint.TryParse(ifMatch.Trim().Trim('"'), out var version))
            throw new ValidationAppException("The If-Match header is not a valid card version.");

        return version;
    }
}
