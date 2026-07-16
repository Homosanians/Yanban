namespace Yanban.Application.Activities;

/// <summary>
/// One entry in a board's activity feed.
///
/// <para><paramref name="OldValue"/>/<paramref name="NewValue"/> are set for renames and null for
/// everything else; a creation has no "before".</para>
/// </summary>
public record ActivityDto(
    long Sequence,
    Guid BoardId,
    Guid ActorId,
    string ActorDisplayName,
    string Action,
    string EntityType,
    Guid EntityId,
    string? Summary,
    string? OldValue,
    string? NewValue,
    DateTimeOffset CreatedAt);

/// <summary>
/// What the audit feed is being asked for. Every field is optional and they compose; an empty query
/// is the plain newest-first feed.
///
/// <para><c>BeforeSequence</c> is the keyset cursor (the smallest Sequence already seen).
/// <c>Search</c> is full-text over the summary and both sides of a rename. <c>ActorId</c> is a
/// filter, not a text match: matching a name as text would also hit every card that mentions it.</para>
/// </summary>
public record ActivityQuery(
    int Limit,
    long? BeforeSequence = null,
    string? Search = null,
    Guid? ActorId = null,
    string? Action = null,
    string? EntityType = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null);
