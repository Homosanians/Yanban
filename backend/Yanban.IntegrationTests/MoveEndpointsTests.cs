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

/// <summary>
/// Drag-and-drop <c>move</c> semantics and, most importantly, the concurrency story:
/// the target list acts as a row-lock mutex so N simultaneous moves into it serialize
/// and cannot collide on a rank; a forced rebalance keeps ranks valid when a slot runs
/// out of gap.
/// </summary>
[Collection("api")]
public class MoveEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public MoveEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"mv_{Guid.NewGuid():N}@example.com";

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

    private async Task<CardDto> CreateCardAsync(HttpClient client, string token, Guid boardId, Guid listId, string title)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token, new { title }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
    }

    private async Task<List<CardDto>> ListCardsAsync(HttpClient client, string token, Guid boardId, Guid listId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/lists/{listId}/cards", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<List<CardDto>>(Json))!;
    }

    private Task<HttpResponseMessage> MoveAsync(HttpClient client, string token, Guid boardId, Guid cardId, Guid targetListId, int position) =>
        client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/cards/{cardId}/move", token,
            new { targetListId, position }));

    [Fact]
    public async Task Move_WithinList_ReordersCards()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var a = await CreateCardAsync(client, token, board.Id, list.Id, "A");
        var b = await CreateCardAsync(client, token, board.Id, list.Id, "B");
        var c = await CreateCardAsync(client, token, board.Id, list.Id, "C");

        // Move C to the front.
        (await MoveAsync(client, token, board.Id, c.Id, list.Id, 0)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var titles = (await ListCardsAsync(client, token, board.Id, list.Id)).Select(x => x.Title).ToList();
        titles.ShouldBe(new[] { "C", "A", "B" });
    }

    [Fact]
    public async Task Move_AcrossLists_ChangesListId()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var source = await CreateListAsync(client, token, board.Id, "Source");
        var target = await CreateListAsync(client, token, board.Id, "Target");
        var card = await CreateCardAsync(client, token, board.Id, source.Id, "Card");

        var res = await MoveAsync(client, token, board.Id, card.Id, target.Id, 0);
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<CardDto>(Json))!.ListId.ShouldBe(target.Id);

        (await ListCardsAsync(client, token, board.Id, source.Id)).ShouldBeEmpty();
        (await ListCardsAsync(client, token, board.Id, target.Id)).ShouldContain(x => x.Id == card.Id);
    }

    [Fact]
    public async Task Move_ToListInAnotherBoard_IsNotFound()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var boardA = await CreateBoardAsync(client, token, "A");
        var boardB = await CreateBoardAsync(client, token, "B");
        var listA = await CreateListAsync(client, token, boardA.Id);
        var listB = await CreateListAsync(client, token, boardB.Id);
        var card = await CreateCardAsync(client, token, boardA.Id, listA.Id, "Card");

        // The card is addressed under its own board A, but the target list belongs to B —
        // a cross-board move must not be possible.
        var res = await MoveAsync(client, token, boardA.Id, card.Id, listB.Id, 0);
        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Viewer_CannotMoveCard()
    {
        var client = NewClient();
        var (ownerToken, _) = await RegisterAsync(client);
        var (viewerToken, viewerEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = viewerEmail, role = "Viewer" }));
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id, "Card");

        (await MoveAsync(client, viewerToken, board.Id, card.Id, list.Id, 0))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ArchivedBoard_BlocksMove()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id, "Card");

        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/archive", token)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await MoveAsync(client, token, board.Id, card.Id, list.Id, 0))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Move_RepeatedlyToFront_TriggersRebalanceYetKeepsRanksDistinctAndOrdered()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);

        const int count = 20;
        var cards = new List<CardDto>();
        for (var i = 0; i < count; i++)
            cards.Add(await CreateCardAsync(client, token, board.Id, list.Id, $"C{i}"));

        // Moving a *distinct* card to the front each time halves the smallest rank, so
        // the head slot runs out of gap after ~16 moves and forces a rebalance. When the
        // list is re-spaced, the smallest rank jumps back up — that upward step is the
        // observable proof the rebalance branch executed.
        var rebalanceObserved = false;
        var previousMin = (await ListCardsAsync(client, token, board.Id, list.Id))[0].Rank;

        for (var i = 1; i < count; i++)
        {
            (await MoveAsync(client, token, board.Id, cards[i].Id, list.Id, 0))
                .StatusCode.ShouldBe(HttpStatusCode.OK);

            var ranks = (await ListCardsAsync(client, token, board.Id, list.Id)).Select(x => x.Rank).ToList();

            ranks.Count.ShouldBe(count);
            ranks.Distinct().Count().ShouldBe(count);                          // no collisions
            ranks.ShouldBe(ranks.OrderBy(r => r, StringComparer.Ordinal));     // still sorted

            if (string.CompareOrdinal(ranks[0], previousMin) > 0)
                rebalanceObserved = true;
            previousMin = ranks[0];
        }

        rebalanceObserved.ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentMovesIntoSameList_SerializeWithoutRankCollisions()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var source = await CreateListAsync(client, token, board.Id, "Source");
        var target = await CreateListAsync(client, token, board.Id, "Target");

        const int n = 8;
        var cards = new List<CardDto>();
        for (var i = 0; i < n; i++)
            cards.Add(await CreateCardAsync(client, token, board.Id, source.Id, $"C{i}"));

        // Fire all moves at once, each into the target's position 0. The target list's
        // row lock forces them to serialize; without it, several would read the same
        // "current front" and compute an identical midpoint -> duplicate ranks.
        var responses = await Task.WhenAll(
            cards.Select(c => MoveAsync(client, token, board.Id, c.Id, target.Id, 0)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        var moved = await ListCardsAsync(client, token, board.Id, target.Id);
        moved.Count.ShouldBe(n);
        moved.Select(x => x.Rank).Distinct().Count().ShouldBe(n);
        (await ListCardsAsync(client, token, board.Id, source.Id)).ShouldBeEmpty();
    }
}
