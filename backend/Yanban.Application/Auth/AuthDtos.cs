using System.ComponentModel.DataAnnotations;

namespace Yanban.Application.Auth;

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(320)] string Email,
    [Required, MinLength(8), MaxLength(200)] string Password,
    [Required, MaxLength(100)] string DisplayName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record RefreshRequest(
    [Required] string RefreshToken);

// Internal service result: carries both tokens.
public record AuthResponse(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

// Browser-facing response: access token only. The refresh token is delivered
// out of band as an httpOnly cookie and never appears in the response body.
public record AccessTokenResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt);

public record UserDto(Guid Id, string Email, string DisplayName);

public record AccessToken(string Value, DateTimeOffset ExpiresAt);
