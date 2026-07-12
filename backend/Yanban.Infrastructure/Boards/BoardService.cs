using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Boards;
using Yanban.Application.Common;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Boards;

public class BoardService : IBoardService
{
    private readonly YanbanDbContext _db;
    private readonly IActivityRecorder _activity;

    public BoardService(YanbanDbContext db, IActivityRecorder activity)
    {
        _db = db;
        _activity = activity;
    }

    public async Task<BoardDto> CreateAsync(Guid userId, CreateBoardRequest request, CancellationToken ct)
    {
        var board = new Board
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Name = request.Name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        // The owner is a first-class member (Admin): effective role is derived solely
        // from membership everywhere, so there is one source of truth for RBAC.
        board.Members.Add(new BoardMember { BoardId = board.Id, UserId = userId, Role = BoardRole.Admin });

        _db.Boards.Add(board);
        _activity.Record(board.Id, ActivityAction.Created, ActivityEntityTypes.Board, board.Id, $"Created \"{board.Name}\"");
        await _db.SaveChangesAsync(ct);

        return new BoardDto(board.Id, board.Name, board.OwnerId, false, board.CreatedAt, BoardRole.Admin);
    }

    public async Task<IReadOnlyList<BoardDto>> ListForUserAsync(Guid userId, CancellationToken ct) =>
        await _db.BoardMembers
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.Board.CreatedAt)
            .Select(m => new BoardDto(m.Board.Id, m.Board.Name, m.Board.OwnerId,
                m.Board.ArchivedAt != null, m.Board.CreatedAt, m.Role))
            .ToListAsync(ct);

    public async Task<BoardDto> GetAsync(Guid userId, Guid boardId, CancellationToken ct) =>
        await _db.BoardMembers
            .Where(m => m.UserId == userId && m.BoardId == boardId)
            .Select(m => new BoardDto(m.Board.Id, m.Board.Name, m.Board.OwnerId,
                m.Board.ArchivedAt != null, m.Board.CreatedAt, m.Role))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundAppException("Board not found.");

    public async Task<BoardDto> RenameAsync(Guid userId, Guid boardId, RenameBoardRequest request, CancellationToken ct)
    {
        var board = await GetBoardAsync(boardId, ct);
        board.Name = request.Name.Trim();
        _activity.Record(boardId, ActivityAction.Updated, ActivityEntityTypes.Board, boardId, $"Renamed to \"{board.Name}\"");
        await _db.SaveChangesAsync(ct);

        var role = await _db.BoardMembers
            .Where(m => m.BoardId == boardId && m.UserId == userId)
            .Select(m => (BoardRole?)m.Role)
            .FirstOrDefaultAsync(ct) ?? BoardRole.Admin;

        return new BoardDto(board.Id, board.Name, board.OwnerId, board.ArchivedAt != null, board.CreatedAt, role);
    }

    public async Task SetArchivedAsync(Guid boardId, bool archived, CancellationToken ct)
    {
        var board = await GetBoardAsync(boardId, ct);
        board.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        _activity.Record(boardId, ActivityAction.Updated, ActivityEntityTypes.Board, boardId, archived ? "Archived" : "Unarchived");
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid boardId, CancellationToken ct)
    {
        var board = await GetBoardAsync(boardId, ct);
        _db.Boards.Remove(board); // members, lists and cards cascade
        // BoardId is an unconstrained column, so this audit row survives the board it
        // records the deletion of (it just won't be reachable via the board's own feed).
        _activity.Record(boardId, ActivityAction.Deleted, ActivityEntityTypes.Board, boardId, $"Deleted \"{board.Name}\"");
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BoardMemberDto>> ListMembersAsync(Guid boardId, CancellationToken ct) =>
        await _db.BoardMembers
            .Where(m => m.BoardId == boardId)
            .OrderBy(m => m.User.Email)
            .Select(m => new BoardMemberDto(m.UserId, m.User.Email, m.User.DisplayName, m.Role))
            .ToListAsync(ct);

    public async Task<BoardMemberDto> AddMemberAsync(Guid boardId, AddMemberRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new NotFoundAppException("No registered user with that email.");

        if (await _db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == user.Id, ct))
            throw new ConflictAppException("User is already a member of this board.");

        _db.BoardMembers.Add(new BoardMember { BoardId = boardId, UserId = user.Id, Role = request.Role });
        _activity.Record(boardId, ActivityAction.Created, ActivityEntityTypes.Member, user.Id, $"Added {user.Email} as {request.Role}");
        await _db.SaveChangesAsync(ct);

        return new BoardMemberDto(user.Id, user.Email, user.DisplayName, request.Role);
    }

    public async Task<BoardMemberDto> UpdateMemberAsync(Guid boardId, Guid targetUserId, UpdateMemberRequest request, CancellationToken ct)
    {
        var board = await GetBoardAsync(boardId, ct);
        if (board.OwnerId == targetUserId)
            throw new ForbiddenAppException("The board owner's role cannot be changed.");

        var member = await _db.BoardMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == targetUserId, ct)
            ?? throw new NotFoundAppException("Member not found.");

        member.Role = request.Role;
        _activity.Record(boardId, ActivityAction.Updated, ActivityEntityTypes.Member, targetUserId, $"Role → {request.Role}");
        await _db.SaveChangesAsync(ct);

        return new BoardMemberDto(member.UserId, member.User.Email, member.User.DisplayName, member.Role);
    }

    public async Task RemoveMemberAsync(Guid boardId, Guid targetUserId, CancellationToken ct)
    {
        var board = await GetBoardAsync(boardId, ct);
        if (board.OwnerId == targetUserId)
            throw new ForbiddenAppException("The board owner cannot be removed.");

        var member = await _db.BoardMembers
            .FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == targetUserId, ct)
            ?? throw new NotFoundAppException("Member not found.");

        // Removing a member must also maintain the "assignee is always a board member"
        // invariant: unassign them from every card on this board. The card FK's SetNull
        // only fires on user *deletion*, not on losing membership, so we do it here — in
        // one transaction with the removal so a card can never point at a non-member.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        _db.BoardMembers.Remove(member);
        _activity.Record(boardId, ActivityAction.Deleted, ActivityEntityTypes.Member, targetUserId, "Removed from board");
        await _db.SaveChangesAsync(ct);

        await _db.Cards
            .Where(c => c.AssigneeId == targetUserId && c.List.BoardId == boardId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AssigneeId, (Guid?)null), ct);

        await tx.CommitAsync(ct);
    }

    private async Task<Board> GetBoardAsync(Guid boardId, CancellationToken ct) =>
        await _db.Boards.FirstOrDefaultAsync(b => b.Id == boardId, ct)
        ?? throw new NotFoundAppException("Board not found.");
}
