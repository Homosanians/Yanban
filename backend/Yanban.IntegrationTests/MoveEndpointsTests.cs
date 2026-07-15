using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
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
    public async Task ConcurrentMovesIntoSameSlot_SerializeWithoutRankCollisions()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var source = await CreateListAsync(client, token, board.Id, "Source");
        var target = await CreateListAsync(client, token, board.Id, "Target");

        // Two fixed anchors in the target; every mover aims for position 1 — the single
        // slot *between* them. Nothing here serializes the movers naturally: they update
        // distinct card rows and only read A and B, and reads don't block under READ
        // COMMITTED. So without the target-list lock each concurrent mover reads the same
        // (A, B) neighbours and computes the identical midpoint -> duplicate ranks. The
        // lock forces them into distinct sub-slots. (Verified load-bearing: with the
        // FOR UPDATE removed, the Distinct() assertion below fails.)
        await CreateCardAsync(client, token, board.Id, target.Id, "A");
        await CreateCardAsync(client, token, board.Id, target.Id, "B");

        const int n = 8;
        var cards = new List<CardDto>();
        for (var i = 0; i < n; i++)
            cards.Add(await CreateCardAsync(client, token, board.Id, source.Id, $"C{i}"));

        var responses = await Task.WhenAll(
            cards.Select(c => MoveAsync(client, token, board.Id, c.Id, target.Id, 1)));

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        var moved = await ListCardsAsync(client, token, board.Id, target.Id);
        moved.Count.ShouldBe(n + 2);
        moved.Select(x => x.Rank).Distinct().Count().ShouldBe(n + 2);   // no collisions
        moved.Select(x => x.Rank).ShouldBe(moved.Select(x => x.Rank).OrderBy(r => r, StringComparer.Ordinal));
        (await ListCardsAsync(client, token, board.Id, source.Id)).ShouldBeEmpty();
    }

    /// <summary>
    /// The conflict the whole move design turns on (ADR-6): two clients grab the <b>same</b> card at
    /// the same version and move it to <b>different</b> lists at the same instant. This is not the
    /// rank-collision race above — those movers touch distinct card rows and the two target-list
    /// locks are different rows, so nothing there serializes <i>this</i> pair. What guards them is the
    /// card's <c>xmin</c> token: exactly one move commits, the other's <c>UPDATE … WHERE xmin=V</c>
    /// matches nothing and surfaces as a 409. First-committer-wins; the card is never duplicated or
    /// stranded.
    ///
    /// <para>Racing two HTTP calls would almost never land inside the window — both would have to read
    /// the card before either commits, and the loser usually just reads the winner's result and moves
    /// <i>that</i> instead (a legitimate sequential move, not a conflict). So the interleaving is
    /// forced: one move is replayed on a held-open transaction that holds the card's row lock at
    /// version V, then the real HTTP move arrives, blocks on that lock, and only wakes — to find V
    /// gone — once the first commits. (Same technique as <c>AssigningWhileTheMemberIsRemoved</c> in
    /// <see cref="ConcurrencyTests"/>.)</para>
    /// </summary>
    [Fact]
    public async Task ConcurrentMovesOfOneCardToDifferentLists_LetExactlyOneThrough()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var source = await CreateListAsync(client, token, board.Id, "Source");
        var toB = await CreateListAsync(client, token, board.Id, "Column B");
        var toC = await CreateListAsync(client, token, board.Id, "Column C");
        var card = await CreateCardAsync(client, token, board.Id, source.Id, "Card");

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var winner = await conn.BeginTransactionAsync();

        // Replay client A's move into column B and stop before commit, holding the card's row lock
        // at the version every mover loaded. Any other UPDATE of this row now blocks here. (Locking
        // the target list too mirrors MoveAsync exactly; it is not what makes this test bite.)
        await ExecAsync(conn, winner, "SELECT id FROM lists WHERE id = $1 FOR UPDATE", toB.Id);
        await ExecAsync(conn, winner, "UPDATE cards SET list_id = $1 WHERE id = $2", toB.Id, card.Id);

        // Client B's move into column C arrives now. It reads the card at the still-committed version
        // V (A's UPDATE is uncommitted, and reads don't block under READ COMMITTED), then blocks on
        // the row lock at its own UPDATE … WHERE xmin = V.
        var loserTask = MoveAsync(client, token, board.Id, card.Id, toC.Id, 0);

        // Long enough that B is parked on the row lock, not still in flight.
        await Task.Delay(1500);
        await winner.CommitAsync();

        // B wakes, finds xmin has moved past V, and its zero-row UPDATE becomes a 409.
        (await loserTask).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The card lives in exactly one place — A's column — neither duplicated nor stranded.
        (await ListCardsAsync(client, token, board.Id, toB.Id)).Select(c => c.Id).ShouldBe(new[] { card.Id });
        (await ListCardsAsync(client, token, board.Id, toC.Id)).ShouldBeEmpty();
        (await ListCardsAsync(client, token, board.Id, source.Id)).ShouldBeEmpty();
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, params object[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var arg in args) cmd.Parameters.AddWithValue(arg);
        await cmd.ExecuteNonQueryAsync();
    }
}
