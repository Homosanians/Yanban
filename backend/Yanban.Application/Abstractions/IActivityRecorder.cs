using Yanban.Domain.Enums;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Records a board mutation into the activity log. The implementation only
/// <c>Add</c>s the row to the same per-request <c>DbContext</c> the caller mutates
/// with — it never calls <c>SaveChanges</c> itself. That is the whole atomicity
/// guarantee: the audit row is flushed by the caller's existing save, inside the
/// caller's transaction, so a change and its audit trail commit or roll back together.
/// </summary>
public interface IActivityRecorder
{
    void Record(Guid boardId, ActivityAction action, string entityType, Guid entityId, string? summary = null);
}
