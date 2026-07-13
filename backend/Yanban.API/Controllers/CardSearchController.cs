using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Search;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// Full-text search over a board's cards (ADR-12). Board-scoped on purpose: search inherits
/// the same ABAC gate as every other endpoint, so it adds no new authorization surface.
/// </summary>
public class CardSearchController : BoardScopedController
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    private readonly ICardSearchService _search;

    public CardSearchController(YanbanDbContext db, IAuthorizationService authz, ICardSearchService search)
        : base(db, authz) => _search = search;

    /// <summary>
    /// Returns the best matches for <paramref name="q"/>, most relevant first. Ranked, not
    /// paged: there is no cursor, because relevance ordering and keyset paging don't compose.
    /// Refine the query instead.
    /// </summary>
    [HttpGet("boards/{boardId:guid}/cards/search")]
    public async Task<ActionResult<IReadOnlyList<CardSearchHit>>> Search(
        Guid boardId, [FromQuery] string q, CancellationToken ct, [FromQuery] int? limit = null)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        return Ok(await _search.SearchAsync(boardId, q, take, ct));
    }
}
