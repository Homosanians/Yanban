using Yanban.Application.Boards;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Board + membership operations. Authorization is enforced upstream (the API's
/// board authorization handler); these methods assume the caller is permitted and
/// focus on data + invariants (e.g. the owner's Admin membership is protected).
/// </summary>
public interface IBoardService
{
    Task<BoardDto> CreateAsync(Guid userId, CreateBoardRequest request, CancellationToken ct);
    Task<IReadOnlyList<BoardDto>> ListForUserAsync(Guid userId, CancellationToken ct);
    Task<BoardDto> GetAsync(Guid userId, Guid boardId, CancellationToken ct);
    Task<BoardDto> RenameAsync(Guid userId, Guid boardId, RenameBoardRequest request, CancellationToken ct);
    Task SetArchivedAsync(Guid boardId, bool archived, CancellationToken ct);
    Task DeleteAsync(Guid boardId, CancellationToken ct);

    Task<IReadOnlyList<BoardMemberDto>> ListMembersAsync(Guid boardId, CancellationToken ct);
    Task<BoardMemberDto> AddMemberAsync(Guid boardId, AddMemberRequest request, CancellationToken ct);
    Task<BoardMemberDto> UpdateMemberAsync(Guid boardId, Guid targetUserId, UpdateMemberRequest request, CancellationToken ct);
    Task RemoveMemberAsync(Guid boardId, Guid targetUserId, CancellationToken ct);
}
