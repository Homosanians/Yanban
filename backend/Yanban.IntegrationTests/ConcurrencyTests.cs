using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
/// M10 — the concurrent paths, attacked rather than assumed.
///
/// Every test here fires real HTTP requests in parallel against the real API and a real
/// Postgres. Each one was watched to <b>fail before the mechanism it guards existed</b>: a
/// concurrency test that has never gone red proves nothing about what it claims to protect.
/// (The ordering equivalent, <see cref="MoveEndpointsTests"/>'s ConcurrentMovesIntoSameSlot,
/// sets the same bar for the target-list FOR UPDATE.)
/// </summary>
[Collection("api")]
public class ConcurrencyTests
{
    private readonly YanbanApiFactory _factory;

    public ConcurrencyTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"cc_{Guid.NewGuid():N}@example.com";

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private static string? RefreshTokenOf(HttpResponseMessage res)
    {
        if (!res.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var cookie in cookies)
        {
            var match = Regex.Match(cookie, "refreshToken=([^;]+)");
            if (match.Success) return Uri.UnescapeDataString(match.Groups[1].Value);
        }
        return null;
    }

    private async Task<(string Access, string Refresh, string Email)> RegisterAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var res = await client.PostAsJsonAsync("/auth/register",
            new { email, password = "correct horse battery staple", displayName = "User" });
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var access = (await res.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;
        return (access, RefreshTokenOf(res)!, email);
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
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token,
            new { title = "Card", description = (string?)null, dueDate = (DateTimeOffset?)null }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
    }

    // ---------------------------------------------------------------- refresh rotation

    /// <summary>
    /// A refresh token is single-use. Presenting the same one twice at the same instant must not
    /// yield two live sessions — that is the exact shape of a stolen token being replayed while
    /// the victim is still active, and it is what reuse detection exists to catch.
    ///
    /// Before the row lock, RefreshAsync read the token, checked RevokedAt, and revoked-and-reissued
    /// across three awaits with nothing serializing them: every racer saw a live token, every racer
    /// won, and the theft signal never fired.
    /// </summary>
    [Fact]
    public async Task ConcurrentRefreshWithTheSameToken_LetsExactlyOneThrough()
    {
        var client = NewClient();
        var (_, refresh, _) = await RegisterAsync(client);

        const int racers = 6;
        var responses = await Task.WhenAll(Enumerable.Range(0, racers).Select(_ =>
            client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh })));

        var winners = responses.Where(r => r.StatusCode == HttpStatusCode.OK).ToList();
        winners.Count.ShouldBe(1, "one token must mint exactly one successor, however many callers race for it");
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(racers - 1);
    }

    /// <summary>
    /// The strict half of the decision: a token presented twice is treated as theft, not as a
    /// benign race. So the winner's *new* token is burned too — the whole family goes.
    ///
    /// The trade-off is real and deliberate (ADR-14): a client that double-submits a refresh gets
    /// logged out everywhere. The alternative — shrugging at a concurrent double-use — means a
    /// stolen token replayed alongside the legitimate one silently succeeds, which would make the
    /// advertised reuse detection a fiction in the one case that matters.
    /// </summary>
    [Fact]
    public async Task ConcurrentRefresh_BurnsTheWholeFamily_NotJustTheLosers()
    {
        var client = NewClient();
        var (_, refresh, _) = await RegisterAsync(client);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh })));

        var winner = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
        var rotated = RefreshTokenOf(winner)!;

        // The successor the winner was handed must itself be dead: the family was revoked the
        // moment the second caller presented the already-spent token.
        var afterwards = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = rotated });
        afterwards.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "a replayed token burns the family, including the successor it had just minted");
    }

    /// <summary>
    /// `logout-all` must mean it. A refresh already in flight when the revoke lands must not be
    /// able to commit a *new* refresh token afterwards and quietly outlive the logout.
    /// </summary>
    [Fact]
    public async Task RefreshRacingLogoutAll_LeavesNoSurvivingSession()
    {
        var client = NewClient();
        var (access, refresh, _) = await RegisterAsync(client);

        var refreshTask = client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        var logoutTask = client.SendAsync(Authed(HttpMethod.Post, "/auth/logout-all", access));

        var results = await Task.WhenAll(refreshTask, logoutTask);
        results[1].StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Whichever way the race fell, nothing minted during it may still work.
        var rotated = results[0].StatusCode == HttpStatusCode.OK ? RefreshTokenOf(results[0]) : null;
        if (rotated is not null)
        {
            var replay = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = rotated });
            replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
                "a token minted by a refresh that raced logout-all must not survive it");
        }

        var original = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh });
        original.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------------------------------------------------------------- membership invariant

    /// <summary>
    /// The invariant <c>BoardService</c> states outright: an assignee is always a board member.
    /// A removal deletes the membership and sweeps the member off every card — but an assignment
    /// running on another connection can check membership (still visible: the delete has not
    /// committed), then write *after* the sweep has already passed. The card is left pointing at
    /// someone who is no longer on the board.
    ///
    /// <para>Racing two HTTP calls will not show this: the window is the few microseconds between
    /// the removal's sweep and its commit, and the assignment almost always just finishes first.
    /// So the interleaving is forced instead — the removal's steps are replayed on a held-open
    /// transaction, which parks the assignment squarely in the window. (Same technique as the
    /// out-of-order outbox test in <see cref="RealtimeHubTests"/>.)</para>
    /// </summary>
    [Fact]
    public async Task AssigningWhileTheMemberIsRemoved_NeverLeavesACardAssignedToANonMember()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);

        var memberClient = NewClient();
        var (_, _, memberEmail) = await RegisterAsync(memberClient);

        var add = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = memberEmail, role = "Editor" }));
        add.StatusCode.ShouldBe(HttpStatusCode.Created);
        var memberId = (await add.Content.ReadFromJsonAsync<BoardMemberDto>(Json))!.UserId;

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var removal = await conn.BeginTransactionAsync();

        // Replay exactly what RemoveMemberAsync does — take the board lock, delete the membership,
        // sweep the assignments — and then stop, holding it all open.
        await ExecAsync(conn, removal, "SELECT id FROM boards WHERE id = $1 FOR UPDATE", board.Id);
        await ExecAsync(conn, removal, "DELETE FROM board_members WHERE board_id = $1 AND user_id = $2",
            board.Id, memberId);
        await ExecAsync(conn, removal, "UPDATE cards SET assignee_id = NULL WHERE assignee_id = $1", memberId);

        // The assignment arrives now: after the sweep, before the commit. Its membership check
        // still sees the member (the delete is uncommitted, and reads don't block).
        var assignTask = client.SendAsync(Authed(HttpMethod.Put,
            $"/boards/{board.Id}/cards/{card.Id}/assignee", ownerToken, new { assigneeId = memberId }));

        // Long enough for an *unguarded* assignment to sail through and commit. A guarded one is
        // still blocked on the board lock at this point, and only proceeds once the removal commits.
        await Task.Delay(1500);
        await removal.CommitAsync();

        await assignTask;

        var reread = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/cards/{card.Id}", ownerToken));
        reread.StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = (await reread.Content.ReadFromJsonAsync<CardDto>(Json))!;

        after.AssigneeId.ShouldBeNull(
            "the member was removed, so no card may still be assigned to them — the invariant BoardService sets out to hold");
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, params object[] args)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var arg in args) cmd.Parameters.AddWithValue(arg);
        await cmd.ExecuteNonQueryAsync();
    }

    // ---------------------------------------------------------------- optimistic concurrency

    /// <summary>
    /// The optimistic-concurrency story the whole card UI leans on (ADR-13): N editors holding the
    /// same ETag, exactly one wins, the rest are told to reload. Nobody's text is silently lost.
    /// </summary>
    [Fact]
    public async Task ConcurrentCardEditsWithTheSameETag_LetExactlyOneThrough()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);

        const int editors = 5;
        var responses = await Task.WhenAll(Enumerable.Range(0, editors).Select(i =>
        {
            var req = Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}", token,
                new { title = $"Edit {i}", description = (string?)null, dueDate = (DateTimeOffset?)null });
            // Every editor holds the version they loaded — the same one.
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{card.Version}\"");
            return client.SendAsync(req);
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed).ShouldBe(editors - 1);
    }
}
