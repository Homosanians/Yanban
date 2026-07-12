using System.ComponentModel.DataAnnotations;

namespace Yanban.Application.Comments;

public record CreateCommentRequest([Required, MaxLength(5000)] string Body);

public record UpdateCommentRequest([Required, MaxLength(5000)] string Body);

public record CommentDto(
    Guid Id,
    Guid CardId,
    Guid AuthorId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt);
