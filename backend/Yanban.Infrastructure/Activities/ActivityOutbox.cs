using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Activities;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Activities;

public class ActivityOutbox : IActivityOutbox
{
    private readonly YanbanDbContext _db;

    public ActivityOutbox(YanbanDbContext db) => _db = db;

    public async Task<long> GetLatestSequenceAsync(CancellationToken ct) =>
        await _db.ActivityLogs.MaxAsync(a => (long?)a.Sequence, ct) ?? 0L;

    public async Task<IReadOnlyList<ActivityDto>> ReadSinceAsync(long afterSequence, int limit, CancellationToken ct)
    {
        // Same join-and-project shape as the board feed (ActivityService), but oldest-first
        // and across every board — one poll serves all of them. No tracking: these rows are
        // read once and pushed out, never mutated.
        var rows = await _db.ActivityLogs
            .AsNoTracking()
            .Where(a => a.Sequence > afterSequence)
            .Join(_db.Users, a => a.ActorId, u => u.Id, (a, u) => new { a, u.DisplayName })
            .OrderBy(x => x.a.Sequence)
            .Take(limit)
            .ToListAsync(ct);

        return rows
            .Select(x => new ActivityDto(
                x.a.Sequence, x.a.BoardId, x.a.ActorId, x.DisplayName,
                x.a.Action.ToString(), x.a.EntityType, x.a.EntityId,
                x.a.Summary, x.a.OldValue, x.a.NewValue, x.a.CreatedAt))
            .ToList();
    }
}
