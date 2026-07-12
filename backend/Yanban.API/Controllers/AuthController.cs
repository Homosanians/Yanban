using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Auth;
using Yanban.Application.Common;

namespace Yanban.API.Controllers;

[ApiController]
[Route("auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "refreshToken";

    private readonly IAuthService _auth;
    private readonly IWebHostEnvironment _env;
    private readonly JwtOptions _jwt;

    public AuthController(IAuthService auth, IWebHostEnvironment env, IOptions<JwtOptions> jwt)
    {
        _auth = auth;
        _env = env;
        _jwt = jwt.Value;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AccessTokenResponse>> Register(RegisterRequest request, CancellationToken ct)
        => IssueTokens(await _auth.RegisterAsync(request, ct));

    [HttpPost("login")]
    public async Task<ActionResult<AccessTokenResponse>> Login(LoginRequest request, CancellationToken ct)
        => IssueTokens(await _auth.LoginAsync(request, ct));

    [HttpPost("refresh")]
    public async Task<ActionResult<AccessTokenResponse>> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RefreshRequest? request,
        CancellationToken ct)
    {
        // Browsers send the token via the httpOnly cookie; the body is a fallback
        // for non-browser clients and tests.
        var token = Request.Cookies[RefreshCookieName] ?? request?.RefreshToken;
        if (string.IsNullOrEmpty(token))
            throw new UnauthorizedAppException("Missing refresh token.");

        return IssueTokens(await _auth.RefreshAsync(token, ct));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var token = Request.Cookies[RefreshCookieName];
        if (!string.IsNullOrEmpty(token))
            await _auth.LogoutAsync(token, ct);

        ClearRefreshCookie();
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await _auth.LogoutAllAsync(CurrentUserId, ct);
        ClearRefreshCookie();
        return NoContent();
    }

    private ActionResult<AccessTokenResponse> IssueTokens(AuthResponse result)
    {
        SetRefreshCookie(result.RefreshToken);
        return Ok(new AccessTokenResponse(result.AccessToken, result.AccessTokenExpiresAt));
    }

    private void SetRefreshCookie(string token) =>
        Response.Cookies.Append(
            RefreshCookieName,
            token,
            BuildCookieOptions(DateTimeOffset.UtcNow.AddDays(_jwt.RefreshTokenDays)));

    private void ClearRefreshCookie() =>
        Response.Cookies.Delete(RefreshCookieName, BuildCookieOptions(expires: null));

    private CookieOptions BuildCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !_env.IsDevelopment(), // localhost dev is http; production is https
        SameSite = SameSiteMode.Strict,
        Path = "/",
        IsEssential = true,
        Expires = expires
    };

    private Guid CurrentUserId => Guid.Parse(User.FindFirst("sub")!.Value);
}
