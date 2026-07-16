using System.Net.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Yanban.IntegrationTests;

/// <summary>
/// CORS is an env-configurable allowlist outside Development. The factory runs the app as
/// <c>Testing</c>, so these exercise the allowlist branch: a listed origin is reflected, an
/// unlisted one is refused. The positive test goes red if CORS is not wired at all (no header);
/// the negative test goes red if the allowlist were secretly reflect-any (an evil origin echoed
/// back).
/// </summary>
[Collection("api")]
public class CorsTests
{
    private readonly YanbanApiFactory _factory;

    public CorsTests(YanbanApiFactory factory) => _factory = factory;

    // Must match Cors__AllowedOrigins__0 in YanbanApiFactory.
    private const string AllowedOrigin = "https://app.yanban.example";

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static HttpRequestMessage Preflight(string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/auth/login");
        req.Headers.Add("Origin", origin);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        return req;
    }

    [Fact]
    public async Task Preflight_FromAnAllowedOrigin_IsReflected_WithCredentials()
    {
        var res = await NewClient().SendAsync(Preflight(AllowedOrigin));

        // Reflected, not "*": AllowCredentials() forbids the wildcard, and the cookie-based refresh
        // needs credentials. Handled by the CORS middleware before the auth rate limiter ever runs.
        res.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain(AllowedOrigin);
        res.Headers.GetValues("Access-Control-Allow-Credentials").ShouldContain("true");
    }

    [Fact]
    public async Task Preflight_FromAnUnlistedOrigin_IsRefused()
    {
        var res = await NewClient().SendAsync(Preflight("https://evil.example"));

        res.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
