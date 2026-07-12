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
using Yanban.Application.Comments;
using Yanban.Application.Lists;

namespace Yanban.IntegrationTests;

/// <summary>
/// M4 — comments (per-comment ABAC: author-only edit, author-or-moderator delete, all
/// gated on Write so an archived board stays read-only) and card assignment (assignee
/// must be a board member).
/// </summary>
[Collection("api")]
public class CommentsAndAssigneeEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public CommentsAndAssigneeEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"ca_{Guid.NewGuid():N}@example.com";

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

    private async Task<CommentDto> CreateCommentAsync(HttpClient client, string token, Guid boardId, Guid cardId, string body)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/cards/{cardId}/comments", token, new { body }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CommentDto>(Json))!;
    }

    // ---- Assignee -------------------------------------------------------------------

    [Fact]
    public async Task Assign_ToBoardMember_SetsThenClearsAssignee()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (_, memberId, memberEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, memberEmail, "Editor");
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);

        var assigned = await client.SendAsync(Authed(HttpMethod.Put,
            $"/boards/{board.Id}/cards/{card.Id}/assignee", ownerToken, new { assigneeId = (Guid?)memberId }));
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await assigned.Content.ReadFromJsonAsync<CardDto>(Json))!.AssigneeId.ShouldBe(memberId);

        var cleared = await client.SendAsync(Authed(HttpMethod.Put,
            $"/boards/{board.Id}/cards/{card.Id}/assignee", ownerToken, new { assigneeId = (Guid?)null }));
        cleared.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cleared.Content.ReadFromJsonAsync<CardDto>(Json))!.AssigneeId.ShouldBeNull();
    }

    [Fact]
    public async Task Assign_ToNonMember_IsRejected()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (_, outsiderId, _) = await RegisterAsync(client); // registered, but not on this board
        var board = await CreateBoardAsync(client, ownerToken);
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);

        // A non-member and a nonexistent user both yield the same 400 (no user enumeration).
        (await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/assignee",
            ownerToken, new { assigneeId = (Guid?)outsiderId }))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/assignee",
            ownerToken, new { assigneeId = (Guid?)Guid.NewGuid() }))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---- Comments -------------------------------------------------------------------

    [Fact]
    public async Task Comment_CanBeCreatedAndListed()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);

        var created = await CreateCommentAsync(client, token, board.Id, card.Id, "First comment");
        created.Body.ShouldBe("First comment");
        created.AuthorDisplayName.ShouldBe("User");
        created.EditedAt.ShouldBeNull();

        var listed = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/cards/{card.Id}/comments", token)))
            .Content.ReadFromJsonAsync<List<CommentDto>>(Json);
        listed!.ShouldContain(c => c.Id == created.Id && c.Body == "First comment");
    }

    [Fact]
    public async Task Comment_EditIsAuthorOnly_AndSetsEditedAt()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (editorToken, _, editorEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, editorEmail, "Editor");
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);
        var comment = await CreateCommentAsync(client, ownerToken, board.Id, card.Id, "Original");

        // Another member (even an editor) cannot rewrite someone else's comment.
        (await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/comments/{comment.Id}",
            editorToken, new { body = "Hijacked" }))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var edited = await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/comments/{comment.Id}",
            ownerToken, new { body = "Edited" }));
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await edited.Content.ReadFromJsonAsync<CommentDto>(Json))!;
        dto.Body.ShouldBe("Edited");
        dto.EditedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Comment_DeleteAllowedForAuthorOrModerator_ButNotOtherMembers()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (aToken, _, aEmail) = await RegisterAsync(client);
        var (bToken, _, bEmail) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, aEmail, "Editor");
        await AddMemberAsync(client, ownerToken, board.Id, bEmail, "Editor");
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);

        // Author deletes their own.
        var own = await CreateCommentAsync(client, aToken, board.Id, card.Id, "A's own");
        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}/cards/{card.Id}/comments/{own.Id}", aToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // A non-author, non-moderator member cannot delete.
        var protectedComment = await CreateCommentAsync(client, aToken, board.Id, card.Id, "A's protected");
        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}/cards/{card.Id}/comments/{protectedComment.Id}", bToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The owner (Admin => Manage) moderates it away.
        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}/cards/{card.Id}/comments/{protectedComment.Id}", ownerToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Comment_MutationsBlockedOnArchivedBoard_ButListStillReadable()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);
        var comment = await CreateCommentAsync(client, token, board.Id, card.Id, "Before archive");

        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/archive", token)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var basePath = $"/boards/{board.Id}/cards/{card.Id}/comments";
        // All three mutations gate on Write, which an archived board denies (ABAC) — even
        // for the author/owner, closing the "edit/delete slipped through Read" hole.
        (await client.SendAsync(Authed(HttpMethod.Post, basePath, token, new { body = "New" })))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.SendAsync(Authed(HttpMethod.Put, $"{basePath}/{comment.Id}", token, new { body = "Edit" })))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.SendAsync(Authed(HttpMethod.Delete, $"{basePath}/{comment.Id}", token)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Reading still works on an archived board.
        (await client.SendAsync(Authed(HttpMethod.Get, basePath, token))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Comment_AddressedUnderWrongCard_IsNotFound()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var cardA = await CreateCardAsync(client, token, board.Id, list.Id, "A");
        var cardB = await CreateCardAsync(client, token, board.Id, list.Id, "B");
        var comment = await CreateCommentAsync(client, token, board.Id, cardA.Id, "On A");

        // The comment belongs to card A; addressing it under card B must not resolve.
        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}/cards/{cardB.Id}/comments/{comment.Id}", token)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
