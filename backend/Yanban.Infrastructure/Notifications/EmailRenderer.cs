using System.Net;
using System.Text.Json;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Application.Notifications;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;

namespace Yanban.Infrastructure.Notifications;

/// <summary>
/// Turns an outbox row into an email. Renders <b>only</b> from the row's payload — it never reaches
/// back into the domain, because the domain has moved on: by the time this runs, the card may have
/// been renamed, reassigned or deleted, and the mail is about what happened, not about now.
/// </summary>
public class EmailRenderer
{
    private readonly EmailOptions _options;

    public EmailRenderer(EmailOptions options) => _options = options;

    public OutgoingEmail Render(OutboxMessage message)
    {
        return message.Type switch
        {
            NotificationType.SignupConfirmation => Confirmation(message),
            NotificationType.CardAssigned => Card(message,
                p => $"{p.ActorName} assigned you “{p.CardTitle}”",
                p => $"{p.ActorName} assigned you the card “{p.CardTitle}” on {p.BoardName}."),
            NotificationType.CardUnassigned => Card(message,
                p => $"{p.ActorName} unassigned you from “{p.CardTitle}”",
                p => $"{p.ActorName} unassigned you from the card “{p.CardTitle}” on {p.BoardName}."),
            NotificationType.AssignedCardMoved => Card(message,
                p => $"{p.ActorName} moved “{p.CardTitle}”",
                p => $"{p.ActorName} moved your card “{p.CardTitle}” to {p.ListName ?? "another list"} on {p.BoardName}."),
            NotificationType.CommentCreated => Card(message,
                p => $"{p.ActorName} commented on “{p.CardTitle}”",
                p => $"{p.ActorName} commented on your card “{p.CardTitle}” on {p.BoardName}:\n\n{p.CommentBody}"),
            _ => throw new InvalidOperationException($"No template for {message.Type}.")
        };
    }

    private OutgoingEmail Confirmation(OutboxMessage message)
    {
        var p = Payload<SignupConfirmationPayload>(message);
        // Uri.EscapeDataString, not raw: the token is base64url and safe, but a link built by
        // concatenation is a habit that stops being safe the moment the input changes.
        var link = $"{_options.AppBaseUrl.TrimEnd('/')}/confirm-email?token={Uri.EscapeDataString(p.Token)}";

        var text =
            $"""
             Hi {p.DisplayName},

             Confirm your email address to secure your Yanban account:

             {link}

             The link is good for 7 days. If you did not sign up, ignore this message.
             """;

        return new OutgoingEmail(
            message.RecipientEmail,
            "Confirm your Yanban email",
            text,
            Html($"Hi {p.DisplayName},",
                 "Confirm your email address to secure your Yanban account.",
                 link,
                 "Confirm email"));
    }

    private OutgoingEmail Card(
        OutboxMessage message,
        Func<CardNotificationPayload, string> subject,
        Func<CardNotificationPayload, string> body)
    {
        var p = Payload<CardNotificationPayload>(message);
        var link = $"{_options.AppBaseUrl.TrimEnd('/')}/boards/{message.BoardId}";

        return new OutgoingEmail(
            message.RecipientEmail,
            subject(p),
            $"{body(p)}\n\n{link}",
            Html(subject(p), body(p), link, "Open the board"));
    }

    private static T Payload<T>(OutboxMessage message) =>
        JsonSerializer.Deserialize<T>(
            message.Payload ?? throw new InvalidOperationException($"Outbox message {message.Id} has no payload."),
            NotificationOutbox.JsonOptions)
        ?? throw new InvalidOperationException($"Outbox message {message.Id} has an unreadable payload.");

    /// <summary>
    /// Every interpolated value is HTML-encoded. A card title is user input, and it is about to be
    /// dropped into markup and sent to someone else's inbox.
    /// </summary>
    private static string Html(string heading, string body, string link, string cta) =>
        $"""
         <div style="font-family:system-ui,-apple-system,'Segoe UI',sans-serif;color:#1f1e1c;line-height:1.5">
           <h2 style="font-size:17px;margin:0 0 12px">{WebUtility.HtmlEncode(heading)}</h2>
           <p style="margin:0 0 18px;white-space:pre-wrap">{WebUtility.HtmlEncode(body)}</p>
           <a href="{WebUtility.HtmlEncode(link)}"
              style="display:inline-block;background:#c26343;color:#fff;text-decoration:none;
                     padding:9px 16px;border-radius:6px;font-weight:600">{WebUtility.HtmlEncode(cta)}</a>
         </div>
         """;
}
