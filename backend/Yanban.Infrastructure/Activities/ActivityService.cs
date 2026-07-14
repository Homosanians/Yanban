using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Yanban.Application.Abstractions;
using Yanban.Application.Activities;
using Yanban.Application.Common;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;
using Yanban.Infrastructure.Persistence.Configurations;

namespace Yanban.Infrastructure.Activities;

public class ActivityService : IActivityService
{
    private readonly YanbanDbContext _db;

    public ActivityService(YanbanDbContext db) => _db = db;

    public async Task<IReadOnlyList<ActivityDto>> ListAsync(Guid boardId, ActivityQuery query, CancellationToken ct)
    {
        var rows = _db.ActivityLogs.Where(a => a.BoardId == boardId);

        // Keyset paging: "older than the last row I saw" beats OFFSET, which re-scans
        // every skipped row and can drift as new activity arrives at the head. It composes with
        // every filter below — paging deeper into a search works exactly like paging the feed.
        if (query.BeforeSequence is long cursor)
            rows = rows.Where(a => a.Sequence < cursor);

        if (query.ActorId is Guid actor)
            rows = rows.Where(a => a.ActorId == actor);

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            // Parsed, not string-compared: an unknown action is a bad request, not an empty feed
            // that leaves the user wondering whether nothing happened or they mistyped.
            if (!Enum.TryParse<ActivityAction>(query.Action, ignoreCase: true, out var action))
                throw new ValidationAppException($"Unknown action \"{query.Action}\".");
            rows = rows.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType;
            rows = rows.Where(a => a.EntityType == entityType);
        }

        if (query.From is DateTimeOffset from)
            rows = rows.Where(a => a.CreatedAt >= from);

        // Inclusive of the whole day the caller named: `to` arrives as a date, and a user who asks
        // for "up to the 14th" means the end of the 14th, not its first instant.
        if (query.To is DateTimeOffset to)
            rows = rows.Where(a => a.CreatedAt < to);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // websearch_to_tsquery, never to_tsquery — it parses whatever was typed and cannot
            // fail, where to_tsquery raises a syntax error on input as ordinary as a trailing "&"
            // and turns a search box into a 500 (ADR-12).
            //
            // The call must stay inside the expression tree: EF.Functions methods are translation
            // stubs with no CLR implementation, so hoisting one into a local makes EF try to
            // evaluate it client-side, and it throws instead of translating.
            var search = query.Search;
            rows = rows.Where(a =>
                EF.Property<NpgsqlTsVector>(a, ActivityLogConfiguration.SearchVectorProperty)
                    .Matches(EF.Functions.WebSearchToTsQuery(CardConfiguration.TextSearchConfig, search)));
        }

        // ActorId is an unconstrained column (no FK/navigation), so join Users explicitly. Inner
        // join is safe because users are never hard-deleted; were that to change, this would need a
        // left join to keep orphaned audit rows.
        //
        // Ordered by Sequence, not by ts_rank: an audit log is a chronology. "Most relevant" is the
        // wrong answer to "what happened to this board, in order" — the search narrows the feed, it
        // does not re-sort it.
        var page = await rows
            .Join(_db.Users, a => a.ActorId, u => u.Id, (a, u) => new { a, u.DisplayName })
            .OrderByDescending(x => x.a.Sequence)
            .Take(query.Limit)
            .ToListAsync(ct);

        return page
            .Select(x => new ActivityDto(
                x.a.Sequence, x.a.BoardId, x.a.ActorId, x.DisplayName,
                x.a.Action.ToString(), x.a.EntityType, x.a.EntityId,
                x.a.Summary, x.a.OldValue, x.a.NewValue, x.a.CreatedAt))
            .ToList();
    }
}
