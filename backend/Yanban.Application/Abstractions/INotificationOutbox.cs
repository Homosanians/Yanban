using Yanban.Domain.Enums;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Enqueues an email into the transactional outbox.
///
/// <para>Deliberately shaped like <see cref="IActivityRecorder"/>, and for the same reason: the
/// implementation only <c>Add</c>s rows to the same per-request <c>DbContext</c> the caller mutates
/// with — it <b>never</b> calls <c>SaveChanges</c>. So the message is flushed by the caller's own
/// save, inside the caller's transaction. A comment that is rolled back sends no mail; there is no
/// window in which we have promised to email about something that never happened.</para>
///
/// <para>Recipients and preferences are resolved <i>here</i>, at enqueue time, so the worker stays
/// dumb: a row in the table is already a decision to send.</para>
/// </summary>
public interface INotificationOutbox
{
    /// <summary>
    /// Queues <paramref name="type"/> to <paramref name="recipientUserId"/> — unless the recipient
    /// is the person who caused it (you are never mailed about your own action), or their
    /// preferences say no. Both of those are no-ops, not errors.
    /// </summary>
    Task EnqueueAsync(
        NotificationType type,
        Guid recipientUserId,
        Guid? boardId,
        object payload,
        CancellationToken ct);
}
