using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Shouldly;
using Xunit;
using Yanban.Application.Auth;

namespace Yanban.IntegrationTests;

[Collection("api")]
public class AuthEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public AuthEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static object RegisterBody(string email) =>
        new { email, password = "correct horse battery staple", displayName = "Test" };

    private static string UniqueEmail() => $"it_{Guid.NewGuid():N}@example.com";

    private static string? ExtractRefreshToken(HttpResponseMessage res)
    {
        if (!res.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            var match = Regex.Match(cookie, "refreshToken=([^;]+)");
            if (match.Success) return Uri.UnescapeDataString(match.Groups[1].Value);
        }
        return null;
    }

    [Fact]
    public async Task Register_ReturnsAccessTokenOnly_AndSetsHttpOnlyCookie()
    {
        var client = NewClient();

        var res = await client.PostAsJsonAsync("/auth/register", RegisterBody(UniqueEmail()));

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.ShouldNotContain("refreshToken");
        var payload = await res.Content.ReadFromJsonAsync<AccessTokenResponse>();
        payload!.AccessToken.ShouldNotBeNullOrEmpty();

        var setCookie = res.Headers.GetValues("Set-Cookie").First();
        setCookie.ShouldContain("refreshToken=");
        setCookie.ShouldContain("httponly", Case.Insensitive);
        setCookie.ShouldContain("samesite=strict", Case.Insensitive);
    }

    [Fact]
    public async Task Refresh_RotatesRefreshToken()
    {
        var client = NewClient();
        var reg = await client.PostAsJsonAsync("/auth/register", RegisterBody(UniqueEmail()));
        var r1 = ExtractRefreshToken(reg);
        r1.ShouldNotBeNull();

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 });

        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var r2 = ExtractRefreshToken(refresh);
        r2.ShouldNotBeNull();
        r2.ShouldNotBe(r1);
    }

    [Fact]
    public async Task ReusedRefreshToken_RevokesWholeFamily()
    {
        var client = NewClient();
        var reg = await client.PostAsJsonAsync("/auth/register", RegisterBody(UniqueEmail()));
        var r1 = ExtractRefreshToken(reg)!;

        var rotation = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 });
        var r2 = ExtractRefreshToken(rotation)!;

        // Replaying the old token is treated as theft.
        var replay = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r1 });
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The rotated token is revoked as well (whole family).
        var afterReuse = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = r2 });
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Password_IsStoredHashed()
    {
        var client = NewClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register", RegisterBody(email));

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("select password_hash from users where email = @e", conn);
        cmd.Parameters.AddWithValue("e", email);
        var hash = (string?)await cmd.ExecuteScalarAsync();

        hash.ShouldNotBeNull();
        hash!.ShouldStartWith("$2");
        hash.ShouldNotContain("correct horse battery staple");
    }
}
