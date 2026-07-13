namespace Yanban.Application.Search;

/// <summary>
/// A card matched by full-text search, carrying enough context to be shown and located on
/// the board (hence <see cref="ListName"/>). No version/ETag: a hit is a read-only preview —
/// the client fetches the card itself to edit it, and gets the ETag then.
/// </summary>
public record CardSearchHit(
    Guid Id,
    Guid ListId,
    string ListName,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    Guid? AssigneeId);
