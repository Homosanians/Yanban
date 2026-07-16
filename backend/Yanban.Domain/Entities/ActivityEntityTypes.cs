namespace Yanban.Domain.Entities;

/// <summary>
/// Stable entity-type tags written to <see cref="ActivityLog.EntityType"/>. Constants
/// rather than raw string literals at the call sites, so a typo can't silently persist
/// a mislabelled audit row.
/// </summary>
public static class ActivityEntityTypes
{
    public const string Board = "Board";
    public const string List = "List";
    public const string Card = "Card";
    public const string Comment = "Comment";
    public const string Member = "Member";
    public const string Attachment = "Attachment";
    public const string Template = "Template";
}
