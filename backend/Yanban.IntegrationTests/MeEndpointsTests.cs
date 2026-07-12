using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;
using Yanban.Application.Auth;

namespace Yanban.IntegrationTests;

[Collection("api")]
public class MeEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public MeEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"me_{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, string AccessToken, string Email)> RegisterAsync()
    {
        var client = NewClient();
        var email = UniqueEmail();
        var res = await client.PostAsJsonAsync("/auth/register",
            new { email, password = "correct horse battery staple", displayName = "Me" });
        var payload = await res.Content.ReadFromJsonAsync<AccessTokenResponse>();
        return (client, payload!.AccessToken, email);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var res = await NewClient().GetAsync("/me");
        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsCurrentUser()
    {
        var (client, token, email) = await RegisterAsync();
        var req = new HttpRequestMessage(HttpMethod.Get, "/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.SendAsync(req);

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var user = await res.Content.ReadFromJsonAsync<UserDto>();
        user!.Email.ShouldBe(email);
    }

    [Fact]
    public async Task LogoutAll_InvalidatesExistingAccessToken()
    {
        var (client, token, _) = await RegisterAsync();
        var bearer = new AuthenticationHeaderValue("Bearer", token);

        var before = new HttpRequestMessage(HttpMethod.Get, "/me");
        before.Headers.Authorization = bearer;
        (await client.SendAsync(before)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var logout = new HttpRequestMessage(HttpMethod.Post, "/auth/logout-all");
        logout.Headers.Authorization = bearer;
        (await client.SendAsync(logout)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Same token is now rejected: token_version bumped and the cache invalidated.
        var after = new HttpRequestMessage(HttpMethod.Get, "/me");
        after.Headers.Authorization = bearer;
        (await client.SendAsync(after)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
