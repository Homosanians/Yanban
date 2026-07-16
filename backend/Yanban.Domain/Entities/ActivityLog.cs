using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>
/// One audited change to a board. This single table is both the audit trail and the
/// outbox a background worker tails to fan out realtime events, rather than two stores
/// kept in sync.
/// </summary>
/// <remarks>
/// <see cref="BoardId"/> and <see cref="ActorId"/> are plain columns, not foreign
/// keys: an audit record must outlive the thing it audits (a board's deletion is
/// itself an event worth keeping), so the row does not cascade away with its board.
/// </remarks>
public class ActivityLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Monotonic, database-assigned ordering key (identity column). The audit feed
    /// orders by it, and the outbox worker uses it as its tail cursor.
    /// </summary>
    public long Sequence { get; set; }

    public Guid BoardId { get; set; }

    /// <summary>The user who performed the change.</summary>
    public Guid ActorId { get; set; }

    public ActivityAction Action { get; set; }

    /// <summary>Which kind of entity changed; see <see cref="ActivityEntityTypes"/>.</summary>
    public string EntityType { get; set; } = null!;

    public Guid EntityId { get; set; }

    /// <summary>Optional human-readable detail, e.g. "Renamed to Sprint 12".</summary>
    public string? Summary { get; set; }

    /// <summary>
    /// What the value was and what it became, for renames (a card's title, a list's or board's
    /// name). Null for everything else: a creation has no "before", and a move is not a text edit.
    ///
    /// <para>Two plain columns rather than a jsonb diff: the requirement is renames, one string
    /// becoming another, and a general-purpose diff format would be more schema than that needs.</para>
    /// </summary>
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
