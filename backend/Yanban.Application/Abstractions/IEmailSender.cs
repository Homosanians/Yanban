namespace Yanban.Application.Abstractions;

/// <summary>An email, already rendered. Nothing in here knows what a card is.</summary>
public record OutgoingEmail(string ToAddress, string Subject, string TextBody, string HtmlBody);

/// <summary>
/// The one thing the worker cannot do in-process. Behind an interface so the outbox loop can be
/// tested against a fake without an SMTP server in the room — the loop's job (claim exactly once,
/// back off, mark sent) is what is worth testing, and it is independent of how bytes reach a relay.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken ct);
}
