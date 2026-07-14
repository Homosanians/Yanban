namespace Yanban.Application.Common;

public class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host. `mailpit` under Compose; a real relay in production.</summary>
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;

    /// <summary>Blank for Mailpit, which asks for nothing. A real relay will want both.</summary>
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>
    /// Off for Mailpit (plaintext on 1025), on for anything real. Explicit rather than inferred
    /// from the port: guessing at transport security is how you end up shipping credentials in
    /// the clear.
    /// </summary>
    public bool UseStartTls { get; set; }

    public string FromAddress { get; set; } = "no-reply@yanban.local";
    public string FromName { get; set; } = "Yanban";

    /// <summary>Where the confirmation link points — the app's public origin, not the API's.</summary>
    public string AppBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>Upper bound on how long a queued mail waits before the worker looks for it.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Rows claimed per pass. The claim holds a row lock for the duration of the send, so this is
    /// also how much work one worker takes off the table at a time — small on purpose.
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>Retries before a message is left as a dead letter (Failed) for a human to look at.</summary>
    public int MaxAttempts { get; set; } = 5;
}
