using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Yanban.Domain.Authorization;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.API.Authorization;

/// <summary>
/// Resource-based authorization for a <see cref="Board"/>: looks up the caller's
/// membership role, then defers the decision to the pure domain rule set
/// (<see cref="BoardAccess"/>). Registered scoped so it can use the request DbContext.
/// </summary>
public class BoardAuthorizationHandler : AuthorizationHandler<BoardPermissionRequirement, Board>
{
    private readonly YanbanDbContext _db;

    public BoardAuthorizationHandler(YanbanDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        BoardPermissionRequirement requirement,
        Board resource)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
            return;

        var role = await _db.BoardMembers
            .Where(m => m.BoardId == resource.Id && m.UserId == userId)
            .Select(m => (BoardRole?)m.Role)
            .FirstOrDefaultAsync();

        if (BoardAccess.IsAllowed(requirement.Permission, role, resource.OwnerId == userId, resource.ArchivedAt is not null))
            context.Succeed(requirement);
    }
}
