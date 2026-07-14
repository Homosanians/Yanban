using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Auth;
using Yanban.Application.Common;
using Yanban.Domain.Entities;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Auth;

public class AuthService : IAuthService
{
    private readonly YanbanDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly ICacheService _cache;
    private readonly JwtOptions _options;

    public AuthService(
        YanbanDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        ICacheService cache,
        IOptions<JwtOptions> options)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _cache = cache;
        _options = options.Value;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
            throw new ValidationAppException("Email and password are required.");

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new ConflictAppException("Email is already registered.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = _hasher.Hash(request.Password),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email : request.DisplayName.Trim(),
            TokenVersion = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.Users.Add(user);

        var (response, _) = IssueTokens(user);
        await _db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = Normalize(request.Email);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAppException("Invalid email or password.");

        var (response, _) = IssueTokens(user);
        await _db.SaveChangesAsync(ct);
        return response;
    }

    /// <summary>
    /// Rotates a refresh token: the presented one is spent and a successor is issued.
    ///
    /// <para>Serialized on the <b>user row</b> (ADR-14). Rotation is read-then-write across
    /// several statements — check the token is live, revoke it, mint its successor — and with
    /// nothing holding those together, concurrent callers presenting the <i>same</i> token each
    /// saw it live and each won: one token minted several live families, and reuse detection
    /// never fired. Measured, not theorized: 4 of 6 racers got a 200.</para>
    ///
    /// <para>The lock is on the user, not the token, because <see cref="LogoutAllAsync"/> mutates
    /// the same session state and takes the same lock first. Locking the token row instead would
    /// have the two paths grabbing the user and token rows in opposite orders — a deadlock.</para>
    /// </summary>
    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAppException("Missing refresh token.");

        var hash = HashRefreshToken(refreshToken);

        // Unlocked, and only to learn whose session state to lock. Nothing is decided on it.
        var ownerId = await _db.RefreshTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == hash)
            .Select(t => (Guid?)t.UserId)
            .FirstOrDefaultAsync(ct);
        if (ownerId is null)
            throw new UnauthorizedAppException("Invalid refresh token.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // `users` has no concurrency token, so SELECT * is safe for FromSql, and ToListAsync
        // runs the statement verbatim so FOR UPDATE stays at the top level (same idiom as the
        // target-list lock in CardService.MoveAsync).
        var locked = await _db.Users
            .FromSql($"SELECT * FROM users WHERE id = {ownerId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
            throw new UnauthorizedAppException("Invalid refresh token.");

        var user = locked[0];

        // Read the token *after* the lock: a caller that queued behind another rotation wakes up
        // and sees what actually committed, not the snapshot it arrived with.
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct)
            ?? throw new UnauthorizedAppException("Invalid refresh token.");

        // Reuse detection: a revoked token being presented signals theft. This is now also where
        // the loser of a concurrent rotation lands — it presented a token that, by the time it
        // held the lock, had already been spent. Strict by choice: the family burns, including
        // the successor the winner was just handed (ADR-14).
        if (existing.RevokedAt is not null)
        {
            await RevokeAllRefreshTokensAsync(existing.UserId, ct);
            // Commit before throwing: the revocation is the whole point, and an exception would
            // otherwise roll the transaction back and leave the stolen family alive.
            await tx.CommitAsync(ct);
            throw new UnauthorizedAppException("Refresh token reuse detected. All sessions were revoked.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAppException("Refresh token expired.");

        var (response, newToken) = IssueTokens(user);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newToken.Id;
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return response;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        var hash = HashRefreshToken(refreshToken);
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (token is null)
            return;

        token.RevokedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Kills every session: bumps TokenVersion (which invalidates outstanding access tokens on
    /// their next request) and revokes every live refresh token.
    ///
    /// <para>Takes the <b>same user-row lock</b> as <see cref="RefreshAsync"/>, and for the same
    /// reason. Without it, a rotation already in flight could commit its brand-new refresh token
    /// *after* the revoke-all had swept the table — a session that quietly survives "log out
    /// everywhere". With the lock, the two orderings are the only two possible, and both are
    /// safe: the rotation either finishes first (and its successor is then revoked) or wakes to
    /// find its token already dead.</para>
    /// </summary>
    public async Task LogoutAllAsync(Guid userId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var locked = await _db.Users
            .FromSql($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
            throw new NotFoundAppException("User not found.");

        var user = locked[0];
        user.TokenVersion += 1;
        await RevokeAllRefreshTokensAsync(userId, ct);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _cache.Remove(TokenVersionCacheKey(userId));
    }

    private (AuthResponse Response, RefreshToken Token) IssueTokens(User user)
    {
        var access = _jwt.CreateAccessToken(user);

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashRefreshToken(raw),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays)
        };
        _db.RefreshTokens.Add(token);

        return (new AuthResponse(access.Value, raw, access.ExpiresAt), token);
    }

    private Task RevokeAllRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();

    private static string HashRefreshToken(string raw)
        => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static string TokenVersionCacheKey(Guid userId) => $"tv:{userId}";
}
