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

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new UnauthorizedAppException("Missing refresh token.");

        var hash = HashRefreshToken(refreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (existing is null)
            throw new UnauthorizedAppException("Invalid refresh token.");

        // Reuse detection: a revoked token being presented signals theft.
        if (existing.RevokedAt is not null)
        {
            await RevokeAllRefreshTokensAsync(existing.UserId, ct);
            throw new UnauthorizedAppException("Refresh token reuse detected. All sessions were revoked.");
        }

        if (existing.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAppException("Refresh token expired.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == existing.UserId, ct);
        if (user is null)
            throw new UnauthorizedAppException("Invalid refresh token.");

        var (response, newToken) = IssueTokens(user);
        existing.RevokedAt = DateTimeOffset.UtcNow;
        existing.ReplacedByTokenId = newToken.Id;
        await _db.SaveChangesAsync(ct);
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

    public async Task LogoutAllAsync(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            throw new NotFoundAppException("User not found.");

        user.TokenVersion += 1;
        await RevokeAllRefreshTokensAsync(userId, ct);
        await _db.SaveChangesAsync(ct);
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
