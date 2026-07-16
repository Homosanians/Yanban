using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Notifications;

/// <summary>
/// Writes outbox rows into the caller's unit of work. See <see cref="INotificationOutbox"/> for the
/// contract that matters: Add only, never SaveChanges.
/// </summary>
public class NotificationOutbox : INotificationOutbox
{
    private readonly YanbanDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly INotificationPreferenceService _preferences;

    public NotificationOutbox(
        YanbanDbContext db,
        ICurrentUser currentUser,
        INotificationPreferenceService preferences)
    {
        _db = db;
        _currentUser = currentUser;
        _preferences = preferences;
    }

    public async Task EnqueueAsync(
        NotificationType type,
        Guid recipientUserId,
        Guid? boardId,
        object payload,
        CancellationToken ct)
    {
        // You are never mailed about your own doing. Assigning a card to yourself, moving your own
        // card, commenting on your own card are all silent. This applies to activity mail only.
        //
        // SignupConfirmation is exempt: it is transactional and addressed to the account owner by
        // definition. Registration has no actor (`UserId` is null before the account exists), but a
        // resend runs authenticated as you, so without this exemption the guard would eat the
        // confirmation, and POST /auth/resend-confirmation would return 204 while sending no mail.
        if (type != NotificationType.SignupConfirmation && _currentUser.UserId == recipientUserId)
            return;

        if (!await _preferences.IsEnabledAsync(recipientUserId, boardId, type, ct))
            return;

        // The address as it stands now. If the recipient is gone by the time the worker runs, the
        // message still knows where it was going.
        //
        // FindAsync, not a query: at signup the recipient IS the user being registered, and their
        // row is still sitting in the change tracker unsaved. A query would go to the database, find
        // nothing, and drop the confirmation email on the floor.
        var user = await _db.Users.FindAsync([recipientUserId], ct);
        if (user is null)
            return;

        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = type,
            RecipientUserId = recipientUserId,
            RecipientEmail = user.Email,
            BoardId = boardId,
            Payload = JsonSerializer.Serialize(payload, JsonOptions),
            Status = OutboxStatus.Pending,
            Attempts = 0,
            // Claimable immediately; only a failure pushes this out.
            NextAttemptAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
        // No SaveChanges. The caller's save is what makes this real, and what makes a rolled-back
        // mutation take its email down with it.
    }

    /// <summary>camelCase, so the payload reads the same in psql as it does in the worker.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
