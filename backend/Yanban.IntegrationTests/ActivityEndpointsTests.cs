using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;
using Yanban.Application.Activities;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Lists;

namespace Yanban.IntegrationTests;

/// <summary>
/// The activity log. Every board mutation writes an audit row in the same transaction as
/// the change; the board's activity feed surfaces them newest-first. The load-bearing test
/// is <see cref="FailedOptimisticUpdate_WritesNoActivityRow"/>: a rejected 412 update leaves
/// no audit row, proving the row shares the mutation's transaction.
/// </summary>
[Collection("api")]
public class ActivityEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public ActivityEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"act_{Guid.NewGuid():N}@example.com";

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

    private Task AddMemberAsync(HttpClient client, string token, Guid boardId, string email, string role) =>
        client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/members", token, new { email, role }));

    private async Task<ListDto> CreateListAsync(HttpClient client, string token, Guid boardId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists", token, new { name = "List" }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ListDto>(Json))!;
    }

    private async Task<CardDto> CreateCardAsync(HttpClient client, string token, Guid boardId, Guid listId, string title = "Card")
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token, new { title }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
    }

    private async Task<List<ActivityDto>> GetActivityAsync(
        HttpClient client, string token, Guid boardId, int? limit = null, long? before = null)
    {
        var query = new List<string>();
        if (limit is not null) query.Add($"limit={limit}");
        if (before is not null) query.Add($"before={before}");
        var url = $"/boards/{boardId}/activity" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var res = await client.SendAsync(Authed(HttpMethod.Get, url, token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<List<ActivityDto>>(Json))!;
    }

    [Fact]
    public async Task EveryMutationType_AppearsInFeed_NewestFirst()
    {
        var client = NewClient();
        var (ownerToken, ownerId, _) = await RegisterAsync(client);
        var (_, memberId, memberEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, memberEmail, "Editor");
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/cards/{card.Id}/move",
            ownerToken, new { targetListId = list.Id, position = 0 }));
        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/assignee",
            ownerToken, new { assigneeId = (Guid?)memberId }));
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/cards/{card.Id}/comments",
            ownerToken, new { body = "Nice card" }));

        var feed = await GetActivityAsync(client, ownerToken, board.Id);

        // Every write path leaves a semantically-typed trail entry.
        feed.ShouldContain(a => a.Action == "Created" && a.EntityType == "Board");
        feed.ShouldContain(a => a.Action == "Created" && a.EntityType == "Member");
        feed.ShouldContain(a => a.Action == "Created" && a.EntityType == "List");
        feed.ShouldContain(a => a.Action == "Created" && a.EntityType == "Card");
        feed.ShouldContain(a => a.Action == "Moved" && a.EntityType == "Card");
        feed.ShouldContain(a => a.Action == "Assigned" && a.EntityType == "Card");
        feed.ShouldContain(a => a.Action == "Created" && a.EntityType == "Comment");

        // Newest-first, with a strictly decreasing (hence gap-tolerant, distinct) cursor.
        var sequences = feed.Select(a => a.Sequence).ToList();
        sequences.ShouldBe(sequences.OrderByDescending(s => s).ToList());
        sequences.Distinct().Count().ShouldBe(sequences.Count);
        feed[0].EntityType.ShouldBe("Comment"); // the last thing done is at the head

        // The actor is captured: the owner created the board.
        feed.Single(a => a.Action == "Created" && a.EntityType == "Board").ActorId.ShouldBe(ownerId);
    }

    [Fact]
    public async Task Activity_RecordsTheActingUser_NotTheBoardOwner()
    {
        var client = NewClient();
        var (ownerToken, ownerId, _) = await RegisterAsync(client);
        var (editorToken, editorId, editorEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, editorEmail, "Editor");
        var list = await CreateListAsync(client, ownerToken, board.Id);

        // The editor, not the owner, creates the card.
        var card = await CreateCardAsync(client, editorToken, board.Id, list.Id);

        var feed = await GetActivityAsync(client, ownerToken, board.Id);
        var cardCreated = feed.Single(a => a.Action == "Created" && a.EntityType == "Card" && a.EntityId == card.Id);
        cardCreated.ActorId.ShouldBe(editorId);
        cardCreated.ActorId.ShouldNotBe(ownerId);
    }

    [Fact]
    public async Task FailedOptimisticUpdate_WritesNoActivityRow()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);
        var cardUrl = $"/boards/{board.Id}/cards/{card.Id}";

        // The card's initial version is a valid If-Match; the first update succeeds and
        // bumps the version, so replaying the same (now stale) tag must fail with 412.
        var staleTag = new EntityTagHeaderValue($"\"{card.Version}\"");

        var ok = Authed(HttpMethod.Put, cardUrl, token, new { title = "v2" });
        ok.Headers.IfMatch.Add(staleTag);
        (await client.SendAsync(ok)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterSuccess = await GetActivityAsync(client, token, board.Id);
        afterSuccess.Count(a => a.Action == "Updated" && a.EntityType == "Card").ShouldBe(1);

        var stale = Authed(HttpMethod.Put, cardUrl, token, new { title = "v3" });
        stale.Headers.IfMatch.Add(staleTag);
        (await client.SendAsync(stale)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // The rejected update rolled back, and its audit row rolled back with it. Were
        // the recorder to save in its own transaction, this count would be 2.
        var afterReject = await GetActivityAsync(client, token, board.Id);
        afterReject.Count(a => a.Action == "Updated" && a.EntityType == "Card").ShouldBe(1);
    }

    [Fact]
    public async Task ActivityFeed_ReadableByAnyMember_ButNotByNonMembers()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (viewerToken, _, viewerEmail) = await RegisterAsync(client);
        var (outsiderToken, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, viewerEmail, "Viewer");

        // A Viewer has Read, so the feed is visible to them.
        (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/activity", viewerToken)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A non-member cannot read the feed.
        (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/activity", outsiderToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ActivityFeed_PagesByBeforeCursor()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        await CreateListAsync(client, token, board.Id);
        await CreateListAsync(client, token, board.Id);
        await CreateListAsync(client, token, board.Id); // 4 rows total: 1 board + 3 lists

        var page1 = await GetActivityAsync(client, token, board.Id, limit: 2);
        page1.Count.ShouldBe(2);

        // The next page is everything strictly older than the last row we saw.
        var lastSeen = page1[^1].Sequence;
        var page2 = await GetActivityAsync(client, token, board.Id, limit: 2, before: lastSeen);
        page2.ShouldNotBeEmpty();
        page2.ShouldAllBe(a => a.Sequence < lastSeen);
    }
}
