using Yanban.Domain.Enums;

namespace Yanban.Domain.Entities;

/// <summary>
/// One opt-in/opt-out, for one user, one notification type, on one board — or globally.
///
/// <para><b>Only overrides are stored.</b> A user who never touches a toggle has no rows at all;
/// the answer then comes from <c>NotificationDefaults</c>. Resolution runs most-specific-first:
/// this board's override, then the user's global override, then the type's default.</para>
///
/// <para><see cref="BoardId"/> null means "the user's global default for this type". Postgres
/// treats NULLs as distinct in a unique index, so the uniqueness of the global row is enforced by
/// a second, filtered unique index rather than by the composite one (see the EF configuration).</para>
/// </summary>
public class NotificationPreference
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Null = the user's global default for this type.</summary>
    public Guid? BoardId { get; set; }
    public Board? Board { get; set; }

    public NotificationType Type { get; set; }

    public bool Enabled { get; set; }
}
