using Yanban.Application.Abstractions;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Activities;

/// <summary>
/// Adds an <see cref="ActivityLog"/> row to the request's tracked context. It shares
/// the scoped <see cref="YanbanDbContext"/> with the calling service and never saves
/// on its own, so the row is flushed by the caller's <c>SaveChangesAsync</c> — in the
/// same transaction as the change it records.
/// </summary>
public class ActivityRecorder : IActivityRecorder
{
    private readonly YanbanDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ActivityRecorder(YanbanDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public void Record(Guid boardId, ActivityAction action, string entityType, Guid entityId, string? summary = null)
    {
        // Every board mutation runs inside an authenticated, board-authorized request,
        // so an actor is always present; its absence is a wiring bug, not a user error.
        var actorId = _currentUser.UserId
            ?? throw new InvalidOperationException("Cannot record activity without an authenticated user.");

        _db.ActivityLogs.Add(new ActivityLog
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
