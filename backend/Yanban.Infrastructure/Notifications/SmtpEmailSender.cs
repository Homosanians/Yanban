using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;

namespace Yanban.Infrastructure.Notifications;

/// <summary>
/// MailKit over SMTP. A fresh connection per send: this is a low-volume outbox drained by one
/// worker, and a pooled connection would be a cache to keep correct for no measurable gain.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.ToAddress));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder
        {
            TextBody = email.TextBody,
            HtmlBody = email.HtmlBody
        }.ToMessageBody();

        using var client = new SmtpClient();

        // Mailpit speaks plaintext on 1025 and offers no TLS at all; a real relay must have it.
        // Explicit either way: SecureSocketOptions.Auto would quietly accept an unencrypted
        // session against a relay that merely failed to advertise STARTTLS.
        await client.ConnectAsync(
            _options.Host,
            _options.Port,
            _options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None,
            ct);

        if (!string.IsNullOrEmpty(_options.User))
            await client.AuthenticateAsync(_options.User, _options.Password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
