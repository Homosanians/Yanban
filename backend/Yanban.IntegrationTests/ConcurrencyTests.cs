using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Shouldly;
using Xunit;
using Yanban.Application.Activities;
using Yanban.Application.Attachments;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Lists;
using Yanban.Domain.Entities;

namespace Yanban.IntegrationTests;

/// <summary>
/// Concurrency tests: the racy paths, attacked rather than assumed.
///
/// Every test fires real HTTP requests in parallel against the real API and Postgres, and each
/// fails before the mechanism it guards exists. The ordering equivalent is
/// <see cref="MoveEndpointsTests"/>'s ConcurrentMovesIntoSameSlot, covering the target-list
/// FOR UPDATE.
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
    /// yield two live sessions: that is a stolen token replayed while the victim is still active,
    /// which is what reuse detection exists to catch. Without a row lock the read-check-revoke
    /// spans three awaits with nothing serializing them, so every racer sees a live token and wins.
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
    /// A token presented twice is treated as theft, not a benign race, so the winner's new token
    /// is burned too: the whole family goes. The trade-off is deliberate. A client that
    /// double-submits a refresh gets logged out everywhere; the alternative lets a stolen token
    /// replayed alongside the legitimate one silently succeed, making reuse detection a fiction in
    /// the one case that matters.
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
    /// `logout-all` must mean it. A refresh already in flight when the revoke lands must not commit
    /// a new refresh token afterwards and quietly outlive the logout.
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
    /// A removal deletes the membership and sweeps the member off every card, but an assignment on
    /// another connection can pass its membership check (the delete is uncommitted, still visible)
    /// then write after the sweep has passed, leaving the card assigned to a non-member.
    ///
    /// <para>Racing two HTTP calls will not show this: the window is the few microseconds between
    /// the removal's sweep and its commit, and the assignment almost always finishes first. So the
    /// interleaving is forced: the removal's steps are replayed on a held-open transaction that
    /// parks the assignment squarely in the window. Same technique as the out-of-order outbox test
    /// in <see cref="RealtimeHubTests"/>.</para>
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

        // Replay exactly what RemoveMemberAsync does: take the board lock, delete the membership,
        // sweep the assignments, then stop and hold it all open.
        await ExecAsync(conn, removal, "SELECT id FROM boards WHERE id = $1 FOR UPDATE", board.Id);
        await ExecAsync(conn, removal, "DELETE FROM board_members WHERE board_id = $1 AND user_id = $2",
            board.Id, memberId);
        await ExecAsync(conn, removal, "UPDATE cards SET assignee_id = NULL WHERE assignee_id = $1", memberId);

        // The assignment arrives now: after the sweep, before the commit. Its membership check
        // still sees the member (the delete is uncommitted, and reads don't block).
        var assignTask = client.SendAsync(Authed(HttpMethod.Put,
            $"/boards/{board.Id}/cards/{card.Id}/assignee", ownerToken, new { assigneeId = memberId }));

        // Long enough for an unguarded assignment to sail through and commit. A guarded one is
        // still blocked on the board lock here, and only proceeds once the removal commits.
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
    /// Optimistic concurrency the whole card UI leans on: N editors holding the same ETag, exactly
    /// one wins, the rest are told to reload. Nobody's text is silently lost.
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
            // Every editor holds the version they loaded, the same one.
            req.Headers.TryAddWithoutValidation("If-Match", $"\"{card.Version}\"");
            return client.SendAsync(req);
        }));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed).ShouldBe(editors - 1);
    }

    // ---------------------------------------------------------------- attachment completion

    /// <summary>
    /// Completing an upload twice is legitimate: a client whose response was dropped retries, and
    /// <c>CompleteAsync</c> returns early if the attachment is already Ready. But that guard is a
    /// check-then-write with nothing holding it together, and <c>attachments</c> carries no xmin
    /// token, so a second concurrent save does not lose: both callers read Pending, both pass the
    /// size check, and both write an audit row. One upload, two "Attached" events, which the outbox
    /// tailer fans out to every client watching the board.
    ///
    /// <para>Idempotent must mean idempotent in its effects, not just its status code.</para>
    /// </summary>
    [Fact]
    public async Task ConcurrentCompleteOnOneAttachment_RecordsASingleActivityRow()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);

        var bytes = Encoding.UTF8.GetBytes("completed more than once");
        const string contentType = "text/plain";

        var slot = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/cards/{card.Id}/attachments",
            token, new { fileName = "note.txt", contentType, sizeBytes = bytes.Length }));
        slot.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ticket = (await slot.Content.ReadFromJsonAsync<UploadTicketDto>(Json))!;

        using (var raw = new HttpClient())
        {
            var content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue(contentType) } };
            (await raw.PutAsync(ticket.UploadUrl, content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The retry storm: several completes for the same upload, at once.
        const int callers = 5;
        var responses = await Task.WhenAll(Enumerable.Range(0, callers).Select(_ =>
            client.SendAsync(Authed(HttpMethod.Post,
                $"/boards/{board.Id}/cards/{card.Id}/attachments/{ticket.AttachmentId}/complete", token))));

        // Every caller is told the truth: the attachment is ready. Punishing the retry would be
        // the wrong fix; the duplicate audit row is the bug.
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        var feed = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/activity", token));
        feed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var activity = (await feed.Content.ReadFromJsonAsync<IReadOnlyList<ActivityDto>>(Json))!;

        activity.Count(a => a.EntityType == ActivityEntityTypes.Attachment && a.EntityId == ticket.AttachmentId)
            .ShouldBe(1, "one upload is one event, however many times the client asks to complete it");

        // And only one attachment exists, not one per caller.
        var listed = await client.SendAsync(Authed(HttpMethod.Get,
            $"/boards/{board.Id}/cards/{card.Id}/attachments", token));
        (await listed.Content.ReadFromJsonAsync<IReadOnlyList<AttachmentDto>>(Json))!.Count.ShouldBe(1);
    }
}
