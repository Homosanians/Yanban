namespace Yanban.Domain.Entities;

/// <summary>
/// A single-use link token. The raw bytes go out in the email; only their SHA-256 hash is stored —
/// the same rule <see cref="RefreshToken"/> follows, and for the same reason: the database is not
/// where live credentials live.
/// </summary>
public class EmailConfirmationToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set the moment it is redeemed. A second redemption of the same token must fail.</summary>
    public DateTimeOffset? ConsumedAt { get; set; }
}
