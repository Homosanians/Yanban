using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Lists;

namespace Yanban.IntegrationTests;

[Collection("api")]
public class ListsCardsEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public ListsCardsEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"lc_{Guid.NewGuid():N}@example.com";

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

    private async Task<(string Token, string Email)> RegisterAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var reg = await client.PostAsJsonAsync("/auth/register",
            new { email, password = "correct horse battery staple", displayName = "User" });
        reg.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;
        return (token, email);
    }

    private async Task<BoardDto> CreateBoardAsync(HttpClient client, string token, string name = "Board")
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, "/boards", token, new { name }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<BoardDto>(Json))!;
    }

    private async Task<ListDto> CreateListAsync(HttpClient client, string token, Guid boardId, string name = "List")
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists", token, new { name }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ListDto>(Json))!;
    }

    private async Task<CardDto> CreateCardAsync(HttpClient client, string token, Guid boardId, Guid listId, string title = "Card")
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token, new { title }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
    }

    private Task<HttpResponseMessage> AddMemberAsync(HttpClient client, string token, Guid boardId, string email, string role) =>
        client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/members", token, new { email, role }));

    [Fact]
    public async Task ListsAndCards_CanBeCreatedAndListed()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id, "First");

        var lists = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/lists", token)))
            .Content.ReadFromJsonAsync<List<ListDto>>(Json);
        lists!.ShouldContain(l => l.Id == list.Id);

        var cards = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/lists/{list.Id}/cards", token)))
            .Content.ReadFromJsonAsync<List<CardDto>>(Json);
        cards!.ShouldContain(c => c.Id == card.Id && c.Title == "First");
    }

    [Fact]
    public async Task Viewer_CannotCreateList_ButEditorCan()
    {
        var client = NewClient();
        var (ownerToken, _) = await RegisterAsync(client);
        var (viewerToken, viewerEmail) = await RegisterAsync(client);
        var (editorToken, editorEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, viewerEmail, "Viewer");
        await AddMemberAsync(client, ownerToken, board.Id, editorEmail, "Editor");

        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists", viewerToken, new { name = "V" })))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists", editorToken, new { name = "E" })))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ArchivedBoard_BlocksWrites_ButAllowsReads()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        await CreateListAsync(client, token, board.Id);

        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/archive", token)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // ABAC: even the owner/Admin cannot write to an archived board.
        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists", token, new { name = "Nope" })))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Reads still succeed.
        (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/lists", token)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Card_AddressedUnderWrongBoard_IsNotFound()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var boardA = await CreateBoardAsync(client, token, "A");
        var boardB = await CreateBoardAsync(client, token, "B");
        var listA = await CreateListAsync(client, token, boardA.Id);
        var cardA = await CreateCardAsync(client, token, boardA.Id, listA.Id);

        // The caller is a member of board B, but the card belongs to board A.
        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardB.Id}/cards/{cardA.Id}", token));
        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CardUpdate_EnforcesIfMatchConcurrency()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);

        // Capture the ETag BEFORE any mutation; replaying this stale value must fail later.
        var get = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/cards/{card.Id}", token));
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
        var staleEtag = get.Headers.ETag;
        staleEtag.ShouldNotBeNull();

        // First update with the current version succeeds and bumps the version.
        var put1 = Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}", token, new { title = "Updated" });
        put1.Headers.IfMatch.Add(staleEtag);
        var put1Res = await client.SendAsync(put1);
        put1Res.StatusCode.ShouldBe(HttpStatusCode.OK);
        put1Res.Headers.ETag.ShouldNotBeNull();
        put1Res.Headers.ETag.ShouldNotBe(staleEtag);

        // Replaying the stale ETag now fails the precondition (412).
        var put2 = Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}", token, new { title = "Conflict" });
        put2.Headers.IfMatch.Add(staleEtag);
        (await client.SendAsync(put2)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        // Omitting If-Match entirely is rejected (428 Precondition Required).
        var put3 = Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}", token, new { title = "NoHeader" });
        ((int)(await client.SendAsync(put3)).StatusCode).ShouldBe(428);
    }
}
