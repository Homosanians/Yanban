using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Activities;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Activities;

public class ActivityService : IActivityService
{
    private readonly YanbanDbContext _db;

    public ActivityService(YanbanDbContext db) => _db = db;

    public async Task<IReadOnlyList<ActivityDto>> ListAsync(Guid boardId, int limit, long? beforeSequence, CancellationToken ct)
    {
        var query = _db.ActivityLogs.Where(a => a.BoardId == boardId);

        // Keyset paging: "older than the last row I saw" beats OFFSET, which re-scans
        // every skipped row and can drift as new activity arrives at the head.
        if (beforeSequence is long cursor)
            query = query.Where(a => a.Sequence < cursor);

        // ActorId is an unconstrained column (no FK/navigation), so join Users
        // explicitly. Inner join is safe because users are never hard-deleted; were
        // that to change, this would need a left join to keep orphaned audit rows.
        // Order + Take before materializing; Action is projected as the enum and
        // stringified in memory so no enum.ToString() has to translate to SQL.
        var rows = await query
            .Join(_db.Users, a => a.ActorId, u => u.Id, (a, u) => new { a, u.DisplayName })
            .OrderByDescending(x => x.a.Sequence)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(x => new ActivityDto(
                x.a.Sequence, x.a.BoardId, x.a.ActorId, x.DisplayName,
                x.a.Action.ToString(), x.a.EntityType, x.a.EntityId, x.a.Summary, x.a.CreatedAt))
            .ToList();
    }
}
