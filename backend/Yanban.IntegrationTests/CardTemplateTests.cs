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
using Yanban.Application.Templates;
using Yanban.Domain.Entities;

namespace Yanban.IntegrationTests;

/// <summary>Board-scoped card templates (ADR-12): a blueprint stamped onto a card, not a live link.</summary>
[Collection("api")]
public class CardTemplateTests
{
    private readonly YanbanApiFactory _factory;

    public CardTemplateTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"tpl_{Guid.NewGuid():N}@example.com";

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        if (body is not null) req.Content = JsonContent.Create(body);
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

    private async Task<BoardDto> CreateBoardAsync(HttpClient client, string token)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, "/boards", token, new { name = "Board" }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<BoardDto>(Json))!;
    }

    private async Task<ListDto> CreateListAsync(HttpClient client, string token, Guid boardId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists", token, new { name = "Backlog" }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<ListDto>(Json))!;
    }

    private async Task<CardTemplateDto> CreateTemplateAsync(
        HttpClient client, string token, Guid boardId, string name = "Bug report",
        string title = "Bug: ", string? description = "## Steps to reproduce")
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/templates", token,
            new { name, title, description }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardTemplateDto>(Json))!;
    }

    private async Task<IReadOnlyList<CardTemplateDto>> ListTemplatesAsync(HttpClient client, string token, Guid boardId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/templates", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<IReadOnlyList<CardTemplateDto>>(Json))!;
    }

    [Fact]
    public async Task Templates_CanBeCreatedListedUpdatedAndDeleted()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);

        var created = await CreateTemplateAsync(client, token, board.Id);
        created.Name.ShouldBe("Bug report");
        created.BoardId.ShouldBe(board.Id);

        (await ListTemplatesAsync(client, token, board.Id)).Select(t => t.Id).ShouldContain(created.Id);

        var updated = await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/templates/{created.Id}", token,
            new { name = "Incident report", title = "Incident: ", description = "## Impact" }));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<CardTemplateDto>(Json))!.Name.ShouldBe("Incident report");

        var deleted = await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}/templates/{created.Id}", token));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListTemplatesAsync(client, token, board.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Viewer_CannotManageTemplates_ButEditorCan()
    {
        var client = NewClient();
        var (ownerToken, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);

        var (viewerToken, viewerEmail) = await RegisterAsync(client);
        var (editorToken, editorEmail) = await RegisterAsync(client);
        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = viewerEmail, role = "Viewer" }))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = editorEmail, role = "Editor" }))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var body = new { name = "T", title = "T", description = (string?)null };

        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/templates", viewerToken, body)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/templates", editorToken, body)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // A Viewer may still read them — templates follow the board's Read/Write split.
        (await ListTemplatesAsync(client, viewerToken, board.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task CardCreatedFromTemplate_CopiesTitleAndDescription()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var template = await CreateTemplateAsync(client, token, board.Id,
            title: "Bug: unspecified", description: "## Steps to reproduce");

        var res = await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{board.Id}/lists/{list.Id}/cards/from-template", token, new { templateId = template.Id }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);

        var card = (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
        card.Title.ShouldBe("Bug: unspecified");
        card.Description.ShouldBe("## Steps to reproduce");
        card.ListId.ShouldBe(list.Id);

        // It is a real card, on the board, like any other.
        var cards = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/lists/{list.Id}/cards", token));
        (await cards.Content.ReadFromJsonAsync<IReadOnlyList<CardDto>>(Json))!
            .Select(c => c.Id).ShouldContain(card.Id);
    }

    [Fact]
    public async Task CardFromTemplate_TakesTitleOverride_AndIsNotRewrittenByLaterTemplateEdits()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var template = await CreateTemplateAsync(client, token, board.Id, title: "Bug: unspecified");

        var res = await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{board.Id}/lists/{list.Id}/cards/from-template", token,
            new { templateId = template.Id, title = "Bug: login loops forever" }));
        var card = (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
        card.Title.ShouldBe("Bug: login loops forever");

        // Editing the template afterwards must not reach back into cards already stamped.
        (await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/templates/{template.Id}", token,
            new { name = "Bug report", title = "COMPLETELY DIFFERENT", description = "changed" })))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var reread = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/cards/{card.Id}", token));
        (await reread.Content.ReadFromJsonAsync<CardDto>(Json))!.Title.ShouldBe("Bug: login loops forever");
    }

    [Fact]
    public async Task TemplateFromAnotherBoard_IsNotFound()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);

        var theirs = await CreateBoardAsync(client, token);
        var template = await CreateTemplateAsync(client, token, theirs.Id);

        var mine = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, mine.Id);

        var res = await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{mine.Id}/lists/{list.Id}/cards/from-template", token, new { templateId = template.Id }));

        res.StatusCode.ShouldBe(HttpStatusCode.NotFound, "a template id must not be usable across board boundaries");
    }

    [Fact]
    public async Task TemplateChanges_AreAudited()
    {
        var client = NewClient();
        var (token, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var template = await CreateTemplateAsync(client, token, board.Id, name: "Release checklist");

        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/activity", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var activity = (await res.Content.ReadFromJsonAsync<IReadOnlyList<ActivityDto>>(Json))!;

        var entry = activity.ShouldHaveSingleItem(a => a.EntityType == ActivityEntityTypes.Template);
        entry.EntityId.ShouldBe(template.Id);
        entry.Action.ShouldBe("Created");
        entry.Summary.ShouldBe("Added template \"Release checklist\"");
    }
}

file static class ShouldlyHelpers
{
    /// <summary>Asserts exactly one item matches, and returns it.</summary>
    public static T ShouldHaveSingleItem<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        var matches = source.Where(predicate).ToList();
        matches.Count.ShouldBe(1);
        return matches[0];
    }
}
