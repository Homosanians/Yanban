namespace Yanban.Domain.Enums;

/// <summary>
/// Coarse-grained role a user holds on a specific board (the RBAC axis).
/// Stored as a string in the database for readability.
/// </summary>
public enum BoardRole
{
    Viewer = 0,
    Editor = 1,
    Admin = 2
}
