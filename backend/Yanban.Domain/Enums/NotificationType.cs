namespace Yanban.Domain.Enums;

/// <summary>
/// What an outbox message is about. Persisted by <b>name</b> (like every other enum here), so the
/// numbers are free to move and a row is readable in psql.
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// The confirm-your-email link. Not subject to preferences — you cannot opt out of the message
    /// that exists to prove the address works, and it is the one type with no board.
    /// </summary>
    SignupConfirmation,

    CardAssigned,
    CardUnassigned,

    /// <summary>A card *you* are assigned to was moved to another list by someone else.</summary>
    AssignedCardMoved,

    /// <summary>Someone commented on a card you are assigned to.</summary>
    CommentCreated
}
