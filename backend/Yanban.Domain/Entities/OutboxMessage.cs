using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>
/// A transactional outbox row: one email that *will* be sent, written in the same transaction as
/// the change that caused it (ADR-8's rule, applied a second time — see ADR-17).
///
/// <para>Separate from <see cref="ActivityLog"/> on purpose. A signup confirmation has no board,
/// so it could never be an activity row; and <c>activity_logs</c> is the realtime tailer's hot
/// read, which has no business carrying recipients and payloads.</para>
///
/// <para>Claimed by status, never by a sequence cursor: <c>WHERE status = 'Pending' … FOR UPDATE
/// SKIP LOCKED</c> re-scans on every poll, so a row that commits late is picked up whenever it
/// becomes visible. That is precisely the hazard <see cref="Yanban.Domain.Entities.ActivityLog"/>'s
/// consumer needs a grace window for, and precisely the reason this one does not.</para>
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }

    public NotificationType Type { get; set; }

    public Guid RecipientUserId { get; set; }

    /// <summary>
    /// Denormalised at enqueue. The address we promised to write to is the address as it was when
    /// the event happened — and the user may not exist by the time the worker gets here.
    /// </summary>
    public string RecipientEmail { get; set; } = null!;

    /// <summary>Null for <see cref="NotificationType.SignupConfirmation"/>, which precedes any board.</summary>
    public Guid? BoardId { get; set; }

    /// <summary>
    /// jsonb. Everything the worker needs to render the mail without re-reading the domain — a
    /// re-read would be a *stale* read anyway (the card may have moved on since).
    ///
    /// <para>Nulled once sent: a confirmation payload carries a live token, and a spent message has
    /// no business keeping a working credential at rest.</para>
    /// </summary>
    public string? Payload { get; set; }

    public OutboxStatus Status { get; set; }
    public int Attempts { get; set; }

    /// <summary>Not before this. Set on failure to back the retry off; the claim query honours it.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? LastError { get; set; }
}
