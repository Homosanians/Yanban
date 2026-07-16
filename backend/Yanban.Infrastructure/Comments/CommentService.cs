using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Comments;
using Yanban.Application.Common;
using Yanban.Application.Notifications;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Comments;

public class CommentService : ICommentService
{
    private readonly YanbanDbContext _db;
    private readonly IActivityRecorder _activity;
    private readonly INotificationOutbox _outbox;

    public CommentService(YanbanDbContext db, IActivityRecorder activity, INotificationOutbox outbox)
    {
        _db = db;
        _activity = activity;
        _outbox = outbox;
    }

    public async Task<IReadOnlyList<CommentDto>> ListAsync(Guid boardId, Guid cardId, CancellationToken ct)
    {
        await EnsureCardOnBoardAsync(boardId, cardId, ct);

        return await _db.Comments
            .Where(c => c.CardId == cardId)
            .OrderBy(c => c.CreatedAt)
            .Select(Projection)
            .ToListAsync(ct);
    }

    public async Task<CommentDto> CreateAsync(Guid boardId, Guid cardId, Guid authorId, CreateCommentRequest request, CancellationToken ct)
    {
        await EnsureCardOnBoardAsync(boardId, cardId, ct);

        var comment = new Comment
        {
            Id = Guid.NewGuid(),
            CardId = cardId,
            AuthorId = authorId,
            Body = request.Body.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Comments.Add(comment);
        _activity.Record(boardId, ActivityAction.Created, ActivityEntityTypes.Comment, comment.Id, "Commented");

        // A comment notifies the card's assignee, and nobody else. There is no participants or
        // watchers concept in this domain. (The outbox drops it if the assignee is the author,
        // and this type is off by default; see NotificationDefaults.)
        var card = await _db.Cards
            .AsNoTracking()
            .Where(c => c.Id == cardId)
            .Select(c => new { c.Title, c.AssigneeId })
            .FirstAsync(ct);

        if (card.AssigneeId is Guid assignee)
        {
            var boardName = await _db.Boards
                .AsNoTracking()
                .Where(b => b.Id == boardId)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(ct) ?? "a board";

            var authorName = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == authorId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct) ?? "Someone";

            await _outbox.EnqueueAsync(
                NotificationType.CommentCreated,
                assignee,
                boardId,
                new CardNotificationPayload(authorName, boardName, card.Title, cardId, CommentBody: comment.Body),
                ct);
        }

        await _db.SaveChangesAsync(ct);

        return await ToDtoAsync(comment.Id, ct);
    }

    public async Task<CommentDto> UpdateAsync(Guid boardId, Guid cardId, Guid commentId, Guid userId, UpdateCommentRequest request, CancellationToken ct)
    {
        var comment = await FindCommentAsync(boardId, cardId, commentId, ct);

        // Per-comment ABAC: even a board admin cannot rewrite another user's words.
        if (comment.AuthorId != userId)
            throw new ForbiddenAppException("Only the author can edit a comment.");

        comment.Body = request.Body.Trim();
        comment.EditedAt = DateTimeOffset.UtcNow;
        _activity.Record(boardId, ActivityAction.Updated, ActivityEntityTypes.Comment, commentId, "Edited a comment");
        await _db.SaveChangesAsync(ct);

        return await ToDtoAsync(comment.Id, ct);
    }

    public async Task DeleteAsync(Guid boardId, Guid cardId, Guid commentId, Guid userId, bool canModerate, CancellationToken ct)
    {
        var comment = await FindCommentAsync(boardId, cardId, commentId, ct);

        // The author may remove their own comment; a board moderator (Manage) may remove any.
        if (comment.AuthorId != userId && !canModerate)
            throw new ForbiddenAppException("Only the author or a board admin can delete a comment.");

        _db.Comments.Remove(comment);
        _activity.Record(boardId, ActivityAction.Deleted, ActivityEntityTypes.Comment, commentId, "Deleted a comment");
        await _db.SaveChangesAsync(ct);
    }

    private static readonly System.Linq.Expressions.Expression<Func<Comment, CommentDto>> Projection =
        c => new CommentDto(c.Id, c.CardId, c.AuthorId, c.Author.DisplayName, c.Body, c.CreatedAt, c.EditedAt);

    private Task<CommentDto> ToDtoAsync(Guid commentId, CancellationToken ct) =>
        _db.Comments.Where(c => c.Id == commentId).Select(Projection).FirstAsync(ct);

    private async Task EnsureCardOnBoardAsync(Guid boardId, Guid cardId, CancellationToken ct)
    {
        if (!await _db.Cards.AnyAsync(c => c.Id == cardId && c.List.BoardId == boardId, ct))
            throw new NotFoundAppException("Card not found.");
    }

    private async Task<Comment> FindCommentAsync(Guid boardId, Guid cardId, Guid commentId, CancellationToken ct) =>
        await _db.Comments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.CardId == cardId && c.Card.List.BoardId == boardId, ct)
        ?? throw new NotFoundAppException("Comment not found.");
}
