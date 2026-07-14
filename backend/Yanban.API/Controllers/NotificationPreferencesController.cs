using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Yanban.Application.Abstractions;
using Yanban.Application.Notifications;
using Yanban.Domain.Authorization;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// A user's own notification switches for one board. Board-scoped because that is how they are set,
/// but they are always the <i>caller's</i>: there is no endpoint here for editing anyone else's
/// preferences, not even an admin's.
/// </summary>
public class NotificationPreferencesController : BoardScopedController
{
    private readonly INotificationPreferenceService _preferences;

    public NotificationPreferencesController(
        YanbanDbContext db,
        IAuthorizationService authz,
        INotificationPreferenceService preferences)
        : base(db, authz)
    {
        _preferences = preferences;
    }

    [HttpGet("boards/{boardId:guid}/notification-preferences")]
    public async Task<ActionResult<IReadOnlyList<NotificationPreferenceDto>>> List(Guid boardId, CancellationToken ct)
    {
        // Read is the right gate: if you can see the board, you may decide what it mails you about.
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        return Ok(await _preferences.ListForBoardAsync(UserId, boardId, ct));
    }

    [HttpPut("boards/{boardId:guid}/notification-preferences")]
    public async Task<IActionResult> Update(
        Guid boardId,
        UpdateNotificationPreferenceRequest request,
        CancellationToken ct)
    {
        await RequireBoardAsync(boardId, BoardPermission.Read, ct);
        await _preferences.SetAsync(UserId, boardId, request.Type, request.Enabled, ct);
        return NoContent();
    }
}
