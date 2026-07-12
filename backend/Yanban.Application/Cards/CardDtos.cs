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

/// <summary><see cref="Version"/> is the Postgres <c>xmin</c> the caller must echo back as an <c>If-Match</c> ETag to update.</summary>
public record CardDto(
    Guid Id,
    Guid ListId,
    string Title,
    string? Description,
    DateTimeOffset? DueDate,
    string Rank,
    uint Version);
