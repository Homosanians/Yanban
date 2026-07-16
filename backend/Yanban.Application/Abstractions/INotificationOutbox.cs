using Yanban.Domain.Enums;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Enqueues an email into the transactional outbox.
///
/// <para>Like <see cref="IActivityRecorder"/>, the implementation only <c>Add</c>s rows to the
/// same per-request <c>DbContext</c> the caller mutates with; it never calls <c>SaveChanges</c>.
/// The message is flushed by the caller's own save, inside the caller's transaction, so a
/// rolled-back change sends no mail.</para>
///
/// <para>Recipients and preferences are resolved here, at enqueue time, so the worker stays simple:
/// a row in the table is already a decision to send.</para>
/// </summary>
public interface INotificationOutbox
{
    /// <summary>
    /// Queues <paramref name="type"/> to <paramref name="recipientUserId"/>, unless the recipient
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
