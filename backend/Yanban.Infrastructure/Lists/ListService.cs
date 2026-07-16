using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Application.Lists;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Domain.Ordering;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Lists;

public class ListService : IListService
{
    private readonly YanbanDbContext _db;
    private readonly IActivityRecorder _activity;

    public ListService(YanbanDbContext db, IActivityRecorder activity)
    {
        _db = db;
        _activity = activity;
    }

    public async Task<IReadOnlyList<ListDto>> ListAsync(Guid boardId, CancellationToken ct) =>
        await _db.Lists
            .Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Rank)
            .Select(l => new ListDto(l.Id, l.BoardId, l.Name, l.Rank))
            .ToListAsync(ct);

    public async Task<ListDto> CreateAsync(Guid boardId, CreateListRequest request, CancellationToken ct)
    {
        var lastRank = await _db.Lists
            .Where(l => l.BoardId == boardId)
            .OrderByDescending(l => l.Rank)
            .Select(l => l.Rank)
            .FirstOrDefaultAsync(ct);

        var list = new BoardList
        {
            Id = Guid.NewGuid(),
            BoardId = boardId,
            Name = request.Name.Trim(),
            Rank = Rank.After(lastRank)
        };
        _db.Lists.Add(list);
        _activity.Record(boardId, ActivityAction.Created, ActivityEntityTypes.List, list.Id, $"Added list \"{list.Name}\"");
        await _db.SaveChangesAsync(ct);

        return new ListDto(list.Id, list.BoardId, list.Name, list.Rank);
    }

    public async Task<ListDto> RenameAsync(Guid boardId, Guid listId, RenameListRequest request, CancellationToken ct)
    {
        var list = await GetListAsync(boardId, listId, ct);

        // Capture the old name before overwriting; the audit row is the only place it survives.
        var oldName = list.Name;
        list.Name = request.Name.Trim();

        _activity.Record(boardId, ActivityAction.Updated, ActivityEntityTypes.List, listId,
            $"Renamed list to \"{list.Name}\"", oldValue: oldName, newValue: list.Name);
        await _db.SaveChangesAsync(ct);
        return new ListDto(list.Id, list.BoardId, list.Name, list.Rank);
    }

    public async Task DeleteAsync(Guid boardId, Guid listId, CancellationToken ct)
    {
        var list = await GetListAsync(boardId, listId, ct);
        _db.Lists.Remove(list); // cards cascade
        _activity.Record(boardId, ActivityAction.Deleted, ActivityEntityTypes.List, listId, $"Deleted list \"{list.Name}\"");
        await _db.SaveChangesAsync(ct);
    }

    private async Task<BoardList> GetListAsync(Guid boardId, Guid listId, CancellationToken ct) =>
        await _db.Lists.FirstOrDefaultAsync(l => l.Id == listId && l.BoardId == boardId, ct)
        ?? throw new NotFoundAppException("List not found.");
}
