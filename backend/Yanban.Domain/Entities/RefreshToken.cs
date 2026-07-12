namespace Yanban.Domain.Entities;

/// <summary>
/// Opaque refresh token. The raw 64 random bytes are sent to the client (base64);
/// only their SHA-256 hash is persisted here. Rotated on every use.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public string TokenHash { get; set; } = null!;

    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Points at the token that superseded this one (rotation chain).</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}
