using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;
using Yanban.Application.Auth;

namespace Yanban.IntegrationTests;

/// <summary>
/// A REST call re-authenticates on every request, so revoking a token (logout-all) locks
/// the caller out at once. A WebSocket does not: it is authorized once, at the handshake,
/// then stays open. Left alone, a hub connection would outlive the credential that opened
/// it, streaming a live board feed to a session the user had explicitly signed out.
///
/// So the connection is given the token's own lifetime: SignalR's
/// <c>CloseOnAuthenticationExpiration</c> hangs it up when the access token expires, bounding
/// how long a revoked session can keep watching.
/// </summary>
[Collection("api")]
public class HubAuthLifetimeTests
{
    private readonly YanbanApiFactory _factory;

    public HubAuthLifetimeTests(YanbanApiFactory factory) => _factory = factory;

    [Fact]
    public async Task HubConnection_IsClosedWhenItsAccessTokenExpires()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var email = $"exp_{Guid.NewGuid():N}@example.com";
        var reg = await client.PostAsJsonAsync("/auth/register",
            new { email, password = "correct horse battery staple", displayName = "User" });
        var issued = (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;

        // The API only issues tokens in whole minutes, too long to wait on. Mint an equivalent
        // one (same signature, issuer, audience and claims) that expires in seconds. Nothing
        // about the check under test cares how long the lifetime is.
        var real = new JwtSecurityTokenHandler().ReadJwtToken(issued);
        var token = MintToken(
            real.Claims.First(c => c.Type == "sub").Value,
            real.Claims.First(c => c.Type == "tv").Value,
            TimeSpan.FromSeconds(5));

        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/board"), o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                o.WebSocketFactory = async (context, ct) =>
                {
                    var uri = new UriBuilder(context.Uri) { Scheme = Uri.UriSchemeHttp };
                    uri.Query = $"{uri.Query.TrimStart('?')}&access_token={Uri.EscapeDataString(token)}";
                    return await _factory.Server.CreateWebSocketClient().ConnectAsync(uri.Uri, ct);
                };
            })
            .Build();

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ => { closed.TrySetResult(); return Task.CompletedTask; };

        await connection.StartAsync();
        connection.State.ShouldBe(HubConnectionState.Connected);

        // The token lapses while the socket is open and idle. Nothing the client does
        // triggers this; the server has to decide to hang up on its own.
        var hungUp = await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        hungUp.ShouldBe(closed.Task, "the hub kept a connection open past the expiry of the token that authorized it");
        connection.State.ShouldBe(HubConnectionState.Disconnected);

        await connection.DisposeAsync();
    }

    private static string MintToken(string subject, string tokenVersion, TimeSpan lifetime)
    {
        // Mirrors YanbanApiFactory's test JWT configuration.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-signing-key-at-least-32-bytes-long"));

        var jwt = new JwtSecurityToken(
            issuer: "yanban-test",
            audience: "yanban-test",
            claims: new[] { new Claim("sub", subject), new Claim("tv", tokenVersion) },
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
