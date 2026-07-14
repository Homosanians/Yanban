using Microsoft.EntityFrameworkCore;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Application.Notifications;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Notifications;

public class NotificationPreferenceService : INotificationPreferenceService
{
    private readonly YanbanDbContext _db;

    public NotificationPreferenceService(YanbanDbContext db) => _db = db;

    public async Task<bool> IsEnabledAsync(Guid userId, Guid? boardId, NotificationType type, CancellationToken ct)
    {
        // Both candidate rows in one round trip; the caller is on the hot path of a mutation.
        var overrides = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId
                        && p.Type == type
                        && (p.BoardId == boardId || p.BoardId == null))
            .Select(p => new { p.BoardId, p.Enabled })
            .ToListAsync(ct);

        // Most specific wins: this board's answer beats the global one beats the default.
        var forBoard = overrides.FirstOrDefault(p => p.BoardId != null);
        if (forBoard is not null) return forBoard.Enabled;

        var global = overrides.FirstOrDefault(p => p.BoardId == null);
        if (global is not null) return global.Enabled;

        return NotificationDefaults.IsEnabledByDefault(type);
    }

    public async Task<IReadOnlyList<NotificationPreferenceDto>> ListForBoardAsync(
        Guid userId, Guid boardId, CancellationToken ct)
    {
        var overrides = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId && (p.BoardId == boardId || p.BoardId == null))
            .Select(p => new { p.BoardId, p.Type, p.Enabled })
            .ToListAsync(ct);

        return NotificationDefaults.Configurable
            .Select(type =>
            {
                var forBoard = overrides.FirstOrDefault(p => p.Type == type && p.BoardId != null);
                var global = overrides.FirstOrDefault(p => p.Type == type && p.BoardId == null);
                var enabled = forBoard?.Enabled
                              ?? global?.Enabled
                              ?? NotificationDefaults.IsEnabledByDefault(type);
                return new NotificationPreferenceDto(type, enabled);
            })
            .ToList();
    }

    public async Task SetAsync(Guid userId, Guid? boardId, NotificationType type, bool enabled, CancellationToken ct)
    {
        // The one type there is nothing to decide about: you cannot turn off the mail that proves
        // your address exists.
        if (type == NotificationType.SignupConfirmation)
            throw new ValidationAppException("The signup confirmation is not optional.");

        var existing = await _db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.BoardId == boardId && p.Type == type, ct);

        if (existing is null)
        {
            _db.NotificationPreferences.Add(new NotificationPreference
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BoardId = boardId,
                Type = type,
                Enabled = enabled
            });
        }
        else
        {
            existing.Enabled = enabled;
        }

        await _db.SaveChangesAsync(ct);
    }
}
