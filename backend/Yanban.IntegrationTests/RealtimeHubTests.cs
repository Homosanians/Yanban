using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using Shouldly;
using Xunit;
using Yanban.API.Realtime;
using Yanban.Application.Activities;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Lists;

namespace Yanban.IntegrationTests;

/// <summary>
/// Realtime board updates. Nothing is published from the request path: the tailer reads the
/// activity log written inside each mutation's transaction and fans it out to the clients
/// subscribed to that board.
///
/// The load-bearing test is <see cref="OutOfOrderCommit_IsStillDelivered"/>. A sequence
/// number is taken at insert but the row only becomes visible at commit, so the two can
/// disagree; a tailer that trusts sequence order alone loses such an event rather than merely
/// skipping it. Set the grace window to zero and that test fails.
/// </summary>
[Collection("api")]
public class RealtimeHubTests
{
    private readonly YanbanApiFactory _factory;

    public RealtimeHubTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(15);

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"rt_{Guid.NewGuid():N}@example.com";

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<(string Token, Guid UserId, string Email)> RegisterAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var reg = await client.PostAsJsonAsync("/auth/register",
            new { email, password = "correct horse battery staple", displayName = "User" });
        reg.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;

        var me = await (await client.SendAsync(Authed(HttpMethod.Get, "/me", token)))
            .Content.ReadFromJsonAsync<JsonElement>();
        return (token, me.GetProperty("id").GetGuid(), email);
    }

    private async Task<BoardDto> CreateBoardAsync(HttpClient client, string token)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, "/boards", token, new { name = "Board" }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<BoardDto>(Json))!;
    }

    private async Task<ListDto> CreateListAsync(HttpClient client, string token, Guid boardId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists", token, new { name = "List" }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ListDto>(Json))!;
    }

    private async Task<CardDto> CreateCardAsync(HttpClient client, string token, Guid boardId, Guid listId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token, new { title = "Card" }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
    }

    private Task AddMemberAsync(HttpClient client, string token, Guid boardId, string email, string role) =>
        client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/members", token, new { email, role }));

    /// <summary>
    /// A hub connection against the in-memory server. Long polling is the reliable default
    /// over TestServer's handler; the WebSocket path is exercised deliberately by
    /// <see cref="SubscriberOverWebSockets_IsAuthenticatedByQueryStringToken"/>, because
    /// only there does the token travel in the query string.
    /// </summary>
    private async Task<BoardEvents> ConnectAsync(string? token, HttpTransportType transport = HttpTransportType.LongPolling)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/board"), options =>
            {
                options.Transports = transport;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                if (token is not null)
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);

                options.WebSocketFactory = async (context, ct) =>
                {
                    // TestServer has no socket to dial; it hands out an in-memory WebSocket
                    // over http.
                    var uri = new UriBuilder(context.Uri) { Scheme = Uri.UriSchemeHttp };

                    // The .NET client would authenticate a WebSocket with an Authorization
                    // header (and a custom factory bypasses that path anyway). A browser
                    // cannot set headers on a handshake, so the JS client appends the token
                    // to the URL instead, which is the only reason Program.cs reads
                    // ?access_token= at all. Do what the browser does, or this tests
                    // a path no real client takes.
                    if (token is not null)
                        uri.Query = $"{uri.Query.TrimStart('?')}&access_token={Uri.EscapeDataString(token)}";

                    var client = _factory.Server.CreateWebSocketClient();
                    return await client.ConnectAsync(uri.Uri, ct);
                };
            })
            .Build();

        await connection.StartAsync();
        return new BoardEvents(connection);
    }

    [Fact]
    public async Task CardCreated_IsPushedToSubscribedBoardMembers()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (viewerToken, _, viewerEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, viewerEmail, "Viewer");
        var list = await CreateListAsync(client, ownerToken, board.Id);

        await using var viewer = await ConnectAsync(viewerToken);
        await viewer.SubscribeAsync(board.Id);

        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);

        var pushed = await viewer.WaitForActivityAsync(
            a => a.EntityType == "Card" && a.EntityId == card.Id, Patience);
        pushed.Action.ShouldBe("Created");
        pushed.BoardId.ShouldBe(board.Id);
    }

    [Fact]
    public async Task SubscriberOverWebSockets_IsAuthenticatedByQueryStringToken()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);

        // A browser cannot put an Authorization header on a WebSocket handshake, so this
        // connection can only be authenticated by the token SignalR appends to the URL.
        await using var owner = await ConnectAsync(ownerToken, HttpTransportType.WebSockets);
        await owner.SubscribeAsync(board.Id);

        var list = await CreateListAsync(client, ownerToken, board.Id);

        var pushed = await owner.WaitForActivityAsync(a => a.EntityId == list.Id, Patience);
        pushed.EntityType.ShouldBe("List");
    }

    [Fact]
    public async Task NonMember_CannotSubscribeToBoard()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (outsiderToken, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);

        await using var outsider = await ConnectAsync(outsiderToken);

        // Authenticated, but the board's ABAC gate says no: the live feed is not a softer
        // way in than the REST API.
        var ex = await Should.ThrowAsync<HubException>(() => outsider.SubscribeAsync(board.Id));
        ex.Message.ShouldContain("permission");
    }

    [Fact]
    public async Task UnauthenticatedConnection_IsRejected()
    {
        NewClient(); // force the server up
        await Should.ThrowAsync<Exception>(() => ConnectAsync(token: null));
    }

    [Fact]
    public async Task Events_AreScopedToTheirBoard()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var watched = await CreateBoardAsync(client, token);
        var other = await CreateBoardAsync(client, token);

        await using var events = await ConnectAsync(token);
        await events.SubscribeAsync(watched.Id);

        // The same user owns both boards, but only subscribed to one.
        await CreateListAsync(client, token, other.Id);
        var sentinel = await CreateListAsync(client, token, watched.Id);

        // Waiting on the sentinel (an event that must arrive, published after the one that
        // must not) beats sleeping and hoping: by the time it lands, the other board's event
        // has had its chance.
        await events.WaitForActivityAsync(a => a.EntityId == sentinel.Id, Patience);
        events.Received.ShouldAllBe(a => a.BoardId == watched.Id);
    }

    [Fact]
    public async Task RemovedMember_IsEvictedFromTheBoardFeed()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (memberToken, memberId, memberEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, memberEmail, "Editor");

        // The member also watches a board of their own, the sentinel channel that proves
        // the connection is still alive and listening after the eviction.
        var ownBoard = await CreateBoardAsync(client, memberToken);

        await using var member = await ConnectAsync(memberToken);
        await member.SubscribeAsync(board.Id);
        await member.SubscribeAsync(ownBoard.Id);

        var removal = await client.SendAsync(
            Authed(HttpMethod.Delete, $"/boards/{board.Id}/members/{memberId}", ownerToken));
        removal.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await member.WaitForRevocationAsync(board.Id, Patience);

        // Group membership would otherwise outlive authorization: without eviction this list
        // would still be pushed to a user who is no longer on the board.
        var leaked = await CreateListAsync(client, ownerToken, board.Id);
        var sentinel = await CreateListAsync(client, memberToken, ownBoard.Id);

        await member.WaitForActivityAsync(a => a.EntityId == sentinel.Id, Patience);
        member.Received.ShouldNotContain(a => a.EntityId == leaked.Id);
    }

    [Fact]
    public async Task OutOfOrderCommit_IsStillDelivered()
    {
        var client = NewClient();
        var (token, userId, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);

        await using var events = await ConnectAsync(token);
        await events.SubscribeAsync(board.Id);

        // Two writers, interleaved: both take a sequence number, then commit in the opposite
        // order. Not hypothetical: it is what concurrent transactions on an identity column do.
        await using var slow = new NpgsqlConnection(_factory.ConnectionString);
        await using var quick = new NpgsqlConnection(_factory.ConnectionString);
        await slow.OpenAsync();
        await quick.OpenAsync();

        await using var slowTx = await slow.BeginTransactionAsync();
        await using var quickTx = await quick.BeginTransactionAsync();

        var slowEntity = Guid.NewGuid();
        var quickEntity = Guid.NewGuid();
        var slowSequence = await InsertActivityAsync(slow, slowTx, board.Id, userId, slowEntity);
        var quickSequence = await InsertActivityAsync(quick, quickTx, board.Id, userId, quickEntity);
        quickSequence.ShouldBeGreaterThan(slowSequence);

        // The higher sequence becomes visible first, and is delivered.
        await quickTx.CommitAsync();
        await events.WaitForActivityAsync(a => a.EntityId == quickEntity, Patience);

        // Now the laggard lands below the cursor's high-water mark. A tailer that had
        // advanced past it would never look back, and this event would be gone for good.
        await slowTx.CommitAsync();
        var late = await events.WaitForActivityAsync(a => a.EntityId == slowEntity, Patience);
        late.Sequence.ShouldBe(slowSequence);
    }

    /// <summary>
    /// Writes an activity row on a caller-controlled transaction: the only way to hold two open
    /// at once and choose the commit order. The actor and board are real, because the tailer
    /// inner-joins Users; a fabricated ActorId would be dropped and the test above would pass for
    /// the wrong reason.
    /// </summary>
    private static async Task<long> InsertActivityAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid boardId, Guid actorId, Guid entityId)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO activity_logs (id, board_id, actor_id, action, entity_type, entity_id, summary, created_at)
            VALUES (@id, @board_id, @actor_id, 'Created', 'Card', @entity_id, 'Concurrent write', @created_at)
            RETURNING sequence;
            """, connection, transaction);

        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("board_id", boardId);
        cmd.Parameters.AddWithValue("actor_id", actorId);
        cmd.Parameters.AddWithValue("entity_id", entityId);
        cmd.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);

        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    /// <summary>Collects what the server pushed to one connection.</summary>
    private sealed class BoardEvents : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly ConcurrentQueue<ActivityDto> _activities = new();
        private readonly ConcurrentQueue<Guid> _revocations = new();

        public BoardEvents(HubConnection connection)
        {
            _connection = connection;
            connection.On<ActivityDto>(nameof(IBoardClient.ActivityOccurred), _activities.Enqueue);
            connection.On<Guid>(nameof(IBoardClient.BoardAccessRevoked), _revocations.Enqueue);
        }

        public IReadOnlyList<ActivityDto> Received => _activities.ToArray();

        public Task SubscribeAsync(Guid boardId) => _connection.InvokeAsync("Subscribe", boardId);

        public Task<ActivityDto> WaitForActivityAsync(Func<ActivityDto, bool> match, TimeSpan timeout) =>
            WaitAsync(() => _activities.FirstOrDefault(match), "an activity event", timeout);

        public Task WaitForRevocationAsync(Guid boardId, TimeSpan timeout) =>
            WaitAsync(() => _revocations.Contains(boardId) ? (Guid?)boardId : null, "an access revocation", timeout);

        private static async Task<T> WaitAsync<T>(Func<T?> poll, string what, TimeSpan timeout) where T : class
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (poll() is { } hit)
                    return hit;
                await Task.Delay(50);
            }

            throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0}s waiting for {what}.");
        }

        private static async Task WaitAsync(Func<Guid?> poll, string what, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (poll() is not null)
                    return;
                await Task.Delay(50);
            }

            throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0}s waiting for {what}.");
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }
}
