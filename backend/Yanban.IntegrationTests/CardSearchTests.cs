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
using Yanban.Application.Search;
using Yanban.Application.Templates;

namespace Yanban.IntegrationTests;

/// <summary>
/// Full-text search (ADR-12). Everything is asserted through the HTTP API — the tsvector is
/// never touched from here, which keeps the test assembly's Npgsql version skew irrelevant.
/// </summary>
[Collection("api")]
public class CardSearchTests
{
    private readonly YanbanApiFactory _factory;

    public CardSearchTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"srch_{Guid.NewGuid():N}@example.com";

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        if (body is not null) req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<string> RegisterAsync(HttpClient client)
    {
        var reg = await client.PostAsJsonAsync("/auth/register",
            new { email = UniqueEmail(), password = "correct horse battery staple", displayName = "User" });
        reg.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;
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

    private async Task<CardDto> CreateCardAsync(
        HttpClient client, string token, Guid boardId, Guid listId, string title, string? description = null)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token,
            new { title, description }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
    }

    private async Task<IReadOnlyList<CardSearchHit>> SearchAsync(HttpClient client, string token, Guid boardId, string q)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Get,
            $"/boards/{boardId}/cards/search?q={Uri.EscapeDataString(q)}", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<IReadOnlyList<CardSearchHit>>(Json))!;
    }

    /// <summary>
    /// The test that proves the dictionary choice is real. Stock Postgres 'russian' stems ASCII
    /// words with english_stem and Cyrillic with russian_stem, so *one* column handles both.
    /// Under a 'simple' config neither of these searches would match anything.
    /// </summary>
    [Fact]
    public async Task Search_MatchesStemmedWords_InBothLanguages()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);

        await CreateCardAsync(client, token, board.Id, list.Id, "Deploy the API");
        await CreateCardAsync(client, token, board.Id, list.Id, "Обновить задачи спринта");

        // Neither query is a substring of the stored text: only stemming can connect them.
        var english = await SearchAsync(client, token, board.Id, "deploying");
        english.Select(h => h.Title).ShouldContain("Deploy the API");

        var russian = await SearchAsync(client, token, board.Id, "задача");
        russian.Select(h => h.Title).ShouldContain("Обновить задачи спринта");
    }

    /// <summary>
    /// Proves setweight(A/B) + ts_rank, not insertion order: the description match is created
    /// first, yet the title match must come back first.
    /// </summary>
    [Fact]
    public async Task TitleMatches_OutrankDescriptionMatches()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);

        await CreateCardAsync(client, token, board.Id, list.Id,
            title: "Sprint planning", description: "We should refactor the checkout flow");
        await CreateCardAsync(client, token, board.Id, list.Id,
            title: "Refactor the checkout flow", description: "Long overdue");

        var hits = await SearchAsync(client, token, board.Id, "refactor");

        hits.Count.ShouldBe(2);
        hits[0].Title.ShouldBe("Refactor the checkout flow", "a title hit (weight A) must outrank a body hit (weight B)");
    }

    /// <summary>
    /// The search box must survive whatever a user types. websearch_to_tsquery parses junk into
    /// a sane query; to_tsquery would raise `syntax error in tsquery` and turn this into a 500.
    /// </summary>
    [Theory]
    [InlineData("a & | b !")]
    [InlineData("&&&")]
    [InlineData("\"unbalanced quote")]
    [InlineData("<>")]
    public async Task MalformedQuery_IsNotAnError(string query)
    {
        var client = NewClient();
        var token = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);

        var res = await client.SendAsync(Authed(HttpMethod.Get,
            $"/boards/{board.Id}/cards/search?q={Uri.EscapeDataString(query)}", token));

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Search_IsScopedToItsBoard()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);

        var mine = await CreateBoardAsync(client, token);
        var mineList = await CreateListAsync(client, token, mine.Id);
        await CreateCardAsync(client, token, mine.Id, mineList.Id, "Migrate the database");

        var other = await CreateBoardAsync(client, token);
        var otherList = await CreateListAsync(client, token, other.Id);
        await CreateCardAsync(client, token, other.Id, otherList.Id, "Migrate the database");

        var hits = await SearchAsync(client, token, mine.Id, "migrate");

        hits.Count.ShouldBe(1, "a card on another board must never leak into this board's results");
        hits[0].ListId.ShouldBe(mineList.Id);
        hits[0].ListName.ShouldBe("List");
    }

    [Fact]
    public async Task NonMember_CannotSearchBoard()
    {
        var client = NewClient();
        var ownerToken = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        var list = await CreateListAsync(client, ownerToken, board.Id);
        await CreateCardAsync(client, ownerToken, board.Id, list.Id, "Secret roadmap");

        var strangerToken = await RegisterAsync(client);
        var res = await client.SendAsync(Authed(HttpMethod.Get,
            $"/boards/{board.Id}/cards/search?q=roadmap", strangerToken));

        res.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EmptyQuery_IsRejected()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);

        var blank = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/cards/search?q=%20", token));
        blank.StatusCode.ShouldBe(HttpStatusCode.BadRequest, "a blank query must not dump the board");

        var missing = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/cards/search", token));
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Guards the decision that templates are a separate table: were they flagged cards, their
    /// text would sit in the same search vector and every search would surface blueprints.
    /// </summary>
    [Fact]
    public async Task Templates_AreNotReturnedBySearch()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);

        var created = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/templates", token,
            new { name = "Bug report", title = "Bug: kryptonite exposure", description = "Steps to reproduce" }));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.Content.ReadFromJsonAsync<CardTemplateDto>(Json))!.Title.ShouldBe("Bug: kryptonite exposure");

        // The template exists and contains the term...
        (await SearchAsync(client, token, board.Id, "kryptonite")).ShouldBeEmpty();

        // ...but only a real card created from it is searchable.
        var fromTemplate = await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{board.Id}/lists/{list.Id}/cards/from-template", token,
            new { templateId = (await ListTemplatesAsync(client, token, board.Id))[0].Id }));
        fromTemplate.StatusCode.ShouldBe(HttpStatusCode.Created);

        var hits = await SearchAsync(client, token, board.Id, "kryptonite");
        hits.Count.ShouldBe(1);
        hits[0].Title.ShouldBe("Bug: kryptonite exposure");
    }

    private async Task<IReadOnlyList<CardTemplateDto>> ListTemplatesAsync(HttpClient client, string token, Guid boardId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/templates", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<IReadOnlyList<CardTemplateDto>>(Json))!;
    }
}
