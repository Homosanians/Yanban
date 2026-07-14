using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>
/// What a user gets before they have expressed any opinion. Only *overrides* are stored
/// (see <see cref="NotificationPreference"/>), so this table is the base of every resolution.
/// </summary>
public static class NotificationDefaults
{
    /// <summary>
    /// Defaults are <b>per type</b>, not one blanket "on".
    ///
    /// <para>The three that are on are about a card that is <i>yours</i> — being handed one, having
    /// one taken away, having one moved out from under you. Those are things you would want to be
    /// told. <see cref="NotificationType.CommentCreated"/> is off, because a chatty card would mail
    /// you on every remark; opting *in* to that is a choice, not a rescue.</para>
    /// </summary>
    private static readonly Dictionary<NotificationType, bool> Defaults = new()
    {
        [NotificationType.CardAssigned] = true,
        [NotificationType.CardUnassigned] = true,
        [NotificationType.AssignedCardMoved] = true,
        [NotificationType.CommentCreated] = false
    };

    /// <summary>The types a user may actually toggle — the order the settings panel renders them in.</summary>
    public static IReadOnlyList<NotificationType> Configurable { get; } = Defaults.Keys.ToArray();

    /// <summary>
    /// <see cref="NotificationType.SignupConfirmation"/> is deliberately absent from
    /// <see cref="Defaults"/> and answers true here: you cannot opt out of the message whose whole
    /// purpose is to prove the address works.
    /// </summary>
    public static bool IsEnabledByDefault(NotificationType type) =>
        !Defaults.TryGetValue(type, out var enabled) || enabled;
}
