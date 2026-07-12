namespace Yanban.Domain.Authorization;

/// <summary>
/// The kind of operation a caller wants to perform on a board (or its lists/cards).
/// Resolved against the caller's <see cref="Enums.BoardRole"/> and board attributes
/// by <see cref="BoardAccess"/>.
/// </summary>
public enum BoardPermission
{
    /// <summary>View the board and its contents.</summary>
    Read,

    /// <summary>Mutate board content (lists, cards). Blocked while archived.</summary>
    Write,

    /// <summary>Administer the board itself: rename, archive, manage members.</summary>
    Manage,

    /// <summary>Hard-delete the board. Owner only.</summary>
    Delete
}
