using Yanban.Domain.Enums;

namespace Yanban.Domain.Authorization;

/// <summary>
/// The board authorization rule set: pure function of the caller's role (RBAC) and
/// board attributes (ABAC: ownership, archived state). Kept dependency-free in the
/// domain so the full truth table is unit-testable without HTTP or a database; the
/// ASP.NET <c>BoardAuthorizationHandler</c> is a thin wrapper over this.
/// </summary>
public static class BoardAccess
{
    /// <param name="role">The caller's role on the board, or <c>null</c> if not a member.</param>
    /// <param name="isOwner">Whether the caller owns the board.</param>
    /// <param name="isArchived">Whether the board is archived (read-only).</param>
    public static bool IsAllowed(BoardPermission permission, BoardRole? role, bool isOwner, bool isArchived) =>
        permission switch
        {
            BoardPermission.Read => role is not null,
            BoardPermission.Write => role >= BoardRole.Editor && !isArchived,
            BoardPermission.Manage => role >= BoardRole.Admin,
            BoardPermission.Delete => isOwner,
            _ => false
        };
}
