using Yanban.Domain.Enums;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Records a board mutation into the activity log. The implementation only
/// <c>Add</c>s the row to the same per-request <c>DbContext</c> the caller mutates
/// with; it never calls <c>SaveChanges</c> itself. The audit row is flushed by the
/// caller's existing save, inside the caller's transaction, so a change and its audit
/// trail commit or roll back together.
/// </summary>
public interface IActivityRecorder
{
    /// <summary>
    /// <paramref name="oldValue"/>/<paramref name="newValue"/> record a rename: what the title or
    /// name was, and what it became. Besides the summary, they are the only text that audit search
    /// matches.
    /// </summary>
    void Record(
        Guid boardId,
        ActivityAction action,
        string entityType,
        Guid entityId,
        string? summary = null,
        string? oldValue = null,
        string? newValue = null);
}
