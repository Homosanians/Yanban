namespace Yanban.Application.Activities;

/// <summary>One entry in a board's activity feed.</summary>
public record ActivityDto(
    long Sequence,
    Guid BoardId,
    Guid ActorId,
    string ActorDisplayName,
    string Action,
    string EntityType,
    Guid EntityId,
    string? Summary,
    DateTimeOffset CreatedAt);
