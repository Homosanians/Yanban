using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>
/// What a user gets before they have expressed any opinion. Only overrides are stored
/// (see <see cref="NotificationPreference"/>), so this table is the base of every resolution.
/// </summary>
public static class NotificationDefaults
{
    /// <summary>
    /// Defaults are per type, not one blanket "on". The three on-by-default types are about a card
    /// assigned to you (assigned, unassigned, moved). <see cref="NotificationType.CommentCreated"/>
    /// is off: a busy card would otherwise mail you on every comment.
    /// </summary>
    private static readonly Dictionary<NotificationType, bool> Defaults = new()
    {
        [NotificationType.CardAssigned] = true,
        [NotificationType.CardUnassigned] = true,
        [NotificationType.AssignedCardMoved] = true,
        [NotificationType.CommentCreated] = false
    };

    /// <summary>The types a user may toggle, in the order the settings panel renders them.</summary>
    public static IReadOnlyList<NotificationType> Configurable { get; } = Defaults.Keys.ToArray();

    /// <summary>
    /// <see cref="NotificationType.SignupConfirmation"/> is absent from <see cref="Defaults"/> and
    /// answers true here: you cannot opt out of the message that proves the address works.
    /// </summary>
    public static bool IsEnabledByDefault(NotificationType type) =>
        !Defaults.TryGetValue(type, out var enabled) || enabled;
}
