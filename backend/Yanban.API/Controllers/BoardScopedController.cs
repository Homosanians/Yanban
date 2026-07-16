using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Yanban.API.Authorization;
using Yanban.Application.Common;
using Yanban.Domain.Authorization;
using Yanban.Domain.Entities;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Controllers;

/// <summary>
/// Base for controllers whose actions operate on (or under) a board. Centralizes the
/// "load the board, then enforce a permission on it" step so every board-scoped
/// action goes through the same authorization gate.
/// </summary>
[ApiController]
[Authorize]
public abstract class BoardScopedController : ControllerBase
{
    protected readonly YanbanDbContext Db;
    private readonly IAuthorizationService _authz;

    protected BoardScopedController(YanbanDbContext db, IAuthorizationService authz)
    {
        Db = db;
        _authz = authz;
    }

    protected Guid UserId => Guid.Parse(User.FindFirst("sub")!.Value);

    /// <summary>
    /// Loads the board (404 if it does not exist) and enforces <paramref name="permission"/>
    /// (403 if the caller is not allowed). Returns the loaded, tracked board.
    /// </summary>
    protected async Task<Board> RequireBoardAsync(Guid boardId, BoardPermission permission, CancellationToken ct)
    {
        var board = await Db.Boards.FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundAppException("Board not found.");

        var result = await _authz.AuthorizeAsync(User, board, new BoardPermissionRequirement(permission));
        if (!result.Succeeded)
            throw new ForbiddenAppException("You do not have permission to perform this action.");

        return board;
    }

    /// <summary>
    /// Evaluates a permission on an already-loaded board without throwing, for
    /// actions that branch on a permission (e.g. "author or moderator") rather than
    /// hard-requiring it.
    /// </summary>
    protected async Task<bool> HasPermissionAsync(Board board, BoardPermission permission) =>
        (await _authz.AuthorizeAsync(User, board, new BoardPermissionRequirement(permission))).Succeeded;
}
