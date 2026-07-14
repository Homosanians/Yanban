namespace Yanban.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Bumped on "logout everywhere". Access tokens carry a copy of this value
    /// ("tv" claim); a mismatch invalidates the token despite a valid signature.
    /// </summary>
    public int TokenVersion { get; set; }

    /// <summary>Set when the account is linked to a VK ID (optional flow).</summary>
    public long? VkId { get; set; }

    /// <summary>
    /// Null until the confirmation link is followed. Deliberately *not* a gate on signing in: an
    /// unconfirmed account works, and is nagged by a banner (ADR-16). Blocking login would make
    /// every integration test fetch a token out of the outbox before it could do anything.
    /// </summary>
    public DateTimeOffset? EmailConfirmedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
