using System.ComponentModel.DataAnnotations;

namespace Yanban.Application.Cards;

public record CreateCardRequest(
    [Required, MaxLength(500)] string Title,
    [MaxLength(10_000)] string? Description,
    DateTimeOffset? DueDate);

public record UpdateCardRequest(
    [Required, MaxLength(500)] string Title,
    [MaxLength(10_000)] string? Description,
    DateTimeOffset? DueDate);

/// <summary>
/// Moves a card to <see cref="TargetListId"/> at <see cref="Position"/> (0-based index
/// among the target list's other cards; out-of-range values are clamped to the ends).
/// </summary>
public record MoveCardRequest(
    [Required] Guid TargetListId,
    [Range(0, int.MaxValue)] int Position);

/// <summary>Sets or clears (null) the card's assignee; the assignee must be a board member.</summary>
public record AssignCardRequest(Guid? AssigneeId);

/// <summary><see cref="Version"/> is the Postgres <c>xmin</c> the caller must echo back as an <c>If-Match</c> ETag to update.</summary>
public record CardDto(
    Guid Id,
    Guid ListId,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    string Rank,
    uint Version,
    Guid? AssigneeId,
    Guid CreatedById);
