using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Yanban.Application.Abstractions;
using Yanban.Application.Auth;
using Yanban.Application.Common;
using Yanban.Domain.Entities;

namespace Yanban.Infrastructure.Security;

public class JwtTokenService : IJwtTokenService
{
    private static readonly JsonWebTokenHandler Handler = new();
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public AccessToken CreateAccessToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));

        // Claims are intentionally identity-only: sub, tv, jti (+ display name).
        // Per-board roles are NOT embedded here -- they change too often and are
        // always resolved against current state at authorization time.
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,   // iat
            NotBefore = now.UtcDateTime,  // nbf
            Expires = expires.UtcDateTime, // exp
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                ["tv"] = user.TokenVersion,
                ["name"] = user.DisplayName
            }
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }
}
