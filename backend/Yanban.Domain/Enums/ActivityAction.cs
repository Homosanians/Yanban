namespace Yanban.Domain.Enums;

/// <summary>
/// The kind of change an <see cref="Entities.ActivityLog"/> row records. Kept
/// deliberately small: the acted-on entity is carried separately (EntityType), so
/// "Card + Moved" or "Board + Updated" compose without an action per entity. Stored
/// as its name, not an int, so the audit table stays readable straight from SQL.
/// </summary>
public enum ActivityAction
{
    Created,
    Updated,
    Deleted,
    Moved,
    Assigned
}
