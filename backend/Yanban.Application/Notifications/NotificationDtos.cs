using System.ComponentModel.DataAnnotations;
using Yanban.Domain.Enums;

namespace Yanban.Application.Notifications;

/// <summary>One toggle as the settings panel sees it: the type, and where the answer came from.</summary>
public record NotificationPreferenceDto(NotificationType Type, bool Enabled);

public record UpdateNotificationPreferenceRequest(
    [EnumDataType(typeof(NotificationType))] NotificationType Type,
    bool Enabled);

/// <summary>
/// The payloads the worker renders from. They are snapshots on purpose — the worker must not go
/// back to the domain to ask what a card is called now, because "now" is after the fact.
/// </summary>
public record SignupConfirmationPayload(string DisplayName, string Token);

public record CardNotificationPayload(
    string ActorName,
    string BoardName,
    string CardTitle,
    Guid CardId,
    string? ListName = null,
    string? CommentBody = null);
