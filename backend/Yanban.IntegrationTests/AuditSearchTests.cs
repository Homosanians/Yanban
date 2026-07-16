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
/// The audit log grows a memory and a search box.
///
/// <para>The load-bearing one is <see cref="RenamingACard_RecordsWhatItWasCalled"/>: an audit trail
/// that says "Updated" without saying from what cannot answer the only question anyone asks of it.
/// And <see cref="Search_NarrowsTheFeed_ButDoesNotReorderIt"/> pins the other half: a search over a
/// chronology must stay a chronology.</para>
/// </summary>
[Collection("api")]
public class AuditSearchTests
{
    private readonly YanbanApiFactory _factory;

    public AuditSearchTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"aud_{Guid.NewGuid():N}@example.com";

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

    private async Task<(string Token, Guid UserId)> RegisterAsync(HttpClient client)
    {
        var reg = await client.PostAsJsonAsync("/auth/register",
            new { email = UniqueEmail(), password = "correct horse battery staple", displayName = "Auditor" });
        var token = (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;
        var me = await (await client.SendAsync(Authed(HttpMethod.Get, "/me", token)))
            .Content.ReadFromJsonAsync<JsonElement>();
        return (token, me.GetProperty("id").GetGuid());
    }

    private async Task<BoardDto> CreateBoardAsync(HttpClient client, string token, string name = "Board") =>
        (await (await client.SendAsync(Authed(HttpMethod.Post, "/boards", token, new { name })))
            .Content.ReadFromJsonAsync<BoardDto>(Json))!;

    private async Task<ListDto> CreateListAsync(HttpClient client, string token, Guid boardId, string name = "List") =>
        (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists", token, new { name })))
            .Content.ReadFromJsonAsync<ListDto>(Json))!;

    private async Task<CardDto> CreateCardAsync(HttpClient client, string token, Guid boardId, Guid listId, string title) =>
        (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token, new { title })))
            .Content.ReadFromJsonAsync<CardDto>(Json))!;

    /// <summary>
    /// The card PUT is the one mutation that demands an <c>If-Match</c>; without it the API answers
    /// 428, by design. So an audit test has to speak optimistic concurrency too.
    /// </summary>
    private async Task<HttpResponseMessage> UpdateCardAsync(
        HttpClient client, string token, Guid boardId, CardDto card, string title, string? description = null)
    {
        var req = Authed(HttpMethod.Put, $"/boards/{boardId}/cards/{card.Id}", token,
            new { title, description, dueDate = (DateTimeOffset?)null });
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{card.Version}\"");
        return await client.SendAsync(req);
    }

    private async Task<List<ActivityDto>> FeedAsync(HttpClient client, string token, Guid boardId, string query = "")
    {
        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/activity{query}", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<List<ActivityDto>>(Json))!;
    }

    // --- before / after -----------------------------------------------------

    [Fact]
    public async Task RenamingACard_RecordsWhatItWasCalled()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id, "Fix the login bug");

        var res = await UpdateCardAsync(client, token, board.Id, card, "Fix the logout bug");
        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        var rename = (await FeedAsync(client, token, board.Id))
            .First(a => a.EntityType == "Card" && a.Action == "Updated");

        rename.OldValue.ShouldBe("Fix the login bug");
        rename.NewValue.ShouldBe("Fix the logout bug");
    }

    [Fact]
    public async Task RenamingAList_RecordsWhatItWasCalled()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id, "Todo");

        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/lists/{list.Id}", token,
            new { name = "In progress" }));

        var rename = (await FeedAsync(client, token, board.Id))
            .First(a => a.EntityType == "List" && a.Action == "Updated");

        rename.OldValue.ShouldBe("Todo");
        rename.NewValue.ShouldBe("In progress");
    }

    [Fact]
    public async Task RenamingABoard_RecordsWhatItWasCalled()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token, "Old board");

        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}", token, new { name = "New board" }));

        var rename = (await FeedAsync(client, token, board.Id))
            .First(a => a.EntityType == "Board" && a.Action == "Updated");

        rename.OldValue.ShouldBe("Old board");
        rename.NewValue.ShouldBe("New board");
    }

    /// <summary>
    /// An edit that leaves the title alone is not a rename. Recording "Alpha to Alpha" would be noise
    /// in the one place that exists to be read carefully.
    /// </summary>
    [Fact]
    public async Task EditingOnlyTheDescription_IsNotRecordedAsARename()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id, "Unchanged");

        await UpdateCardAsync(client, token, board.Id, card, "Unchanged", "Now with detail");

        var update = (await FeedAsync(client, token, board.Id))
            .First(a => a.EntityType == "Card" && a.Action == "Updated");

        update.OldValue.ShouldBeNull();
        update.NewValue.ShouldBeNull();
    }

    // --- search -------------------------------------------------------------

    /// <summary>
    /// The search must reach the old name, which is the entire point of keeping it. Someone
    /// hunting for a card they remember by its former title is exactly who this feature is for.
    /// </summary>
    [Fact]
    public async Task Search_FindsARename_ByItsFormerTitle()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id, "Kubernetes migration");

        await UpdateCardAsync(client, token, board.Id, card, "Compose is enough");

        var hits = await FeedAsync(client, token, board.Id, "?q=kubernetes");

        // The rename row's summary reads `Updated "Compose is enough"`; the word "Kubernetes"
        // appears nowhere in it. The only way this row comes back from a search for "kubernetes"
        // is if old_value is in the search vector, which is the whole claim being made here.
        hits.ShouldContain(a => a.Action == "Updated" && a.EntityType == "Card");
        var row = hits.Single(a => a.Action == "Updated");
        row.OldValue.ShouldBe("Kubernetes migration");
        row.NewValue.ShouldBe("Compose is enough");

        // The creation row matches too, on its summary; that is correct, not a leak: the card was
        // once called that, and both facts are part of the same history.
        hits.ShouldContain(a => a.Action == "Created");
    }

    /// <summary>
    /// An audit log is a chronology. The search narrows it; it must not re-sort it by relevance.
    /// "What happened, in order" is the question, and ts_rank does not answer it.
    /// </summary>
    [Fact]
    public async Task Search_NarrowsTheFeed_ButDoesNotReorderIt()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);

        await CreateCardAsync(client, token, board.Id, list.Id, "Widget one");
        await CreateCardAsync(client, token, board.Id, list.Id, "Sprocket");
        await CreateCardAsync(client, token, board.Id, list.Id, "Widget two");

        var hits = await FeedAsync(client, token, board.Id, "?q=widget");

        hits.Count.ShouldBe(2);
        // Newest first, exactly like the unfiltered feed.
        hits[0].Summary.ShouldContain("Widget two");
        hits[1].Summary.ShouldContain("Widget one");
        hits[0].Sequence.ShouldBeGreaterThan(hits[1].Sequence);
    }

    [Fact]
    public async Task Filters_ByActor_AndByEntityType()
    {
        var client = NewClient();
        var (ownerToken, ownerId) = await RegisterAsync(client);
        var (memberToken, memberId) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);
        var me = await (await client.SendAsync(Authed(HttpMethod.Get, "/me", memberToken)))
            .Content.ReadFromJsonAsync<JsonElement>();
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = me.GetProperty("email").GetString(), role = "Editor" }));

        var list = await CreateListAsync(client, ownerToken, board.Id);
        await CreateCardAsync(client, ownerToken, board.Id, list.Id, "Owner's card");
        await CreateCardAsync(client, memberToken, board.Id, list.Id, "Member's card");

        var byMember = await FeedAsync(client, ownerToken, board.Id, $"?actorId={memberId}&entityType=Card");
        byMember.ShouldAllBe(a => a.ActorId == memberId && a.EntityType == "Card");
        byMember.ShouldHaveSingleItem().Summary.ShouldContain("Member's card");

        var byOwner = await FeedAsync(client, ownerToken, board.Id, $"?actorId={ownerId}&entityType=Card");
        byOwner.ShouldHaveSingleItem().Summary.ShouldContain("Owner's card");
    }

    /// <summary>A mistyped action is a bad request, not an empty feed that looks like "nothing happened".</summary>
    [Fact]
    public async Task AnUnknownAction_IsRejected()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);

        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/activity?action=Frobnicated", token));

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// websearch_to_tsquery, not to_tsquery: a user who types a trailing "&" must get an empty
    /// result, not a 500. Anything a search box can produce, the parser has to survive.
    /// </summary>
    [Fact]
    public async Task AGarbageQuery_DoesNotBlowUp()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);

        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/activity?q=%26%20%7C%20!", token));

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
