using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Activities;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// The board's activity feed — the human-readable face of the audit log. Any board
/// member (Read) can view it; it is served newest-first and paged by a Sequence cursor.
/// </summary>
public class ActivityController : BoardScopedController
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private readonly IActivityService _activity;

    public ActivityController(YanbanDbContext db, IAuthorizationService authz, IActivityService activity)
        : base(db, authz) => _activity = activity;

    /// <summary>
    /// Every filter is optional and they compose. With none of them this is the feed it always was,
    /// which is why the existing callers did not have to change.
    /// </summary>
    [HttpGet("boards/{boardId:guid}/activity")]
    public async Task<ActionResult<IReadOnlyList<ActivityDto>>> List(
        Guid boardId,
        CancellationToken ct,
        [FromQuery] int? limit = null,
        [FromQuery] long? before = null,
        [FromQuery] string? q = null,
        [FromQuery] Guid? actorId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? entityType = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);

        var query = new ActivityQuery(
            Limit: Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit),
            BeforeSequence: before,
            Search: q,
            ActorId: actorId,
            Action: action,
            EntityType: entityType,
            From: from,
            To: to);

        return Ok(await _activity.ListAsync(boardId, query, ct));
    }
}
