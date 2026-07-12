namespace Yanban.Domain.Entities;

/// <summary>
/// The stable entity-type tags written to <see cref="ActivityLog.EntityType"/>.
/// Centralized as constants so a typo can't silently persist a mislabelled audit row
/// that no test would catch (a raw string literal at ~18 call sites would).
/// </summary>
public static class ActivityEntityTypes
{
    public const string Board = "Board";
    public const string List = "List";
    public const string Card = "Card";
    public const string Comment = "Comment";
    public const string Member = "Member";
}
