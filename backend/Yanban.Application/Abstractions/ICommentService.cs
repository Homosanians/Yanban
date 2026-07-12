using Yanban.Application.Comments;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Comment operations, scoped board -> card (IDOR-safe). Board-level authorization
/// is enforced by the controller; this layer adds per-comment ABAC: only the author
/// may edit, and only the author or a board moderator may delete.
/// </summary>
public interface ICommentService
{
    Task<IReadOnlyList<CommentDto>> ListAsync(Guid boardId, Guid cardId, CancellationToken ct);
    Task<CommentDto> CreateAsync(Guid boardId, Guid cardId, Guid authorId, CreateCommentRequest request, CancellationToken ct);
    Task<CommentDto> UpdateAsync(Guid boardId, Guid cardId, Guid commentId, Guid userId, UpdateCommentRequest request, CancellationToken ct);
    Task DeleteAsync(Guid boardId, Guid cardId, Guid commentId, Guid userId, bool canModerate, CancellationToken ct);
}
