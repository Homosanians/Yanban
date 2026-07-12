using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>
/// One audited change to a board. This single table is both the audit trail and the
/// outbox a background worker tails (M7) to fan out realtime events — one source of
/// truth rather than two stores kept in sync.
/// </summary>
/// <remarks>
/// <see cref="BoardId"/> and <see cref="ActorId"/> are plain columns, not foreign
/// keys: an audit record must outlive the thing it audits (a board's deletion is
/// itself an event worth keeping), so the row deliberately does not cascade away with
/// its board.
/// </remarks>
public class ActivityLog
{
    public Guid Id { get; set; }

    /// <summary>
    /// Monotonic, database-assigned ordering key (identity column). The audit feed
    /// orders by it, and the M7 outbox worker uses it as its tail cursor.
    /// </summary>
    public long Sequence { get; set; }

    public Guid BoardId { get; set; }

    /// <summary>The user who performed the change.</summary>
    public Guid ActorId { get; set; }

    public ActivityAction Action { get; set; }

    /// <summary>Which kind of entity changed — see <see cref="ActivityEntityTypes"/>.</summary>
    public string EntityType { get; set; } = null!;

    public Guid EntityId { get; set; }

    /// <summary>Optional human-readable detail, e.g. "Renamed to Sprint 12".</summary>
    public string? Summary { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
