using Yanban.Application.Notifications;
using Yanban.Domain.Enums;

namespace Yanban.Application.Abstractions;

/// <summary>
/// Resolves and edits "do you want to hear about this?".
///
/// <para>Resolution is most-specific-first: an override for this board, else the user's global
/// override, else the type's default. Only overrides are stored, so a user who has never touched
/// a toggle has no rows at all.</para>
/// </summary>
public interface INotificationPreferenceService
{
    Task<bool> IsEnabledAsync(Guid userId, Guid? boardId, NotificationType type, CancellationToken ct);

    /// <summary>Every configurable type, resolved for this board; what the settings panel renders.</summary>
    Task<IReadOnlyList<NotificationPreferenceDto>> ListForBoardAsync(Guid userId, Guid boardId, CancellationToken ct);

    Task SetAsync(Guid userId, Guid? boardId, NotificationType type, bool enabled, CancellationToken ct);
}
