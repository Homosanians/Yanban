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
using Yanban.Application.Lists;
using Yanban.Domain.Enums;

namespace Yanban.IntegrationTests;

[Collection("api")]
public class BoardsEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public BoardsEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    // The API serializes enums as names ("Admin"); mirror that when deserializing.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"bo_{Guid.NewGuid():N}@example.com";

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

    /// <summary>Registers a fresh user; returns their access token, id and email.</summary>
    private async Task<(string Token, Guid UserId, string Email)> RegisterAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var reg = await client.PostAsJsonAsync("/auth/register",
            new { email, password = "correct horse battery staple", displayName = "User" });
        reg.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;

        var me = await client.SendAsync(Authed(HttpMethod.Get, "/me", token));
        var user = await me.Content.ReadFromJsonAsync<UserDto>(Json);
        return (token, user!.Id, email);
    }

    private async Task<BoardDto> CreateBoardAsync(HttpClient client, string token, string name = "My Board")
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, "/boards", token, new { name }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<BoardDto>(Json))!;
    }

    private Task<HttpResponseMessage> AddMemberAsync(HttpClient client, string token, Guid boardId, string email, BoardRole role) =>
        client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/members", token,
            new { email, role = role.ToString() }));

    [Fact]
    public async Task CreateBoard_MakesCallerOwnerAndAdmin()
    {
        var client = NewClient();
        var (token, userId, _) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, token);

        board.OwnerId.ShouldBe(userId);
        board.Role.ShouldBe(BoardRole.Admin);
        board.Archived.ShouldBeFalse();

        var members = await (await client.SendAsync(
            Authed(HttpMethod.Get, $"/boards/{board.Id}/members", token)))
            .Content.ReadFromJsonAsync<List<BoardMemberDto>>(Json);
        members!.ShouldContain(m => m.UserId == userId && m.Role == BoardRole.Admin);
    }

    [Fact]
    public async Task List_ReturnsOnlyBoardsWhereCallerIsMember()
    {
        var client = NewClient();
        var (aToken, _, _) = await RegisterAsync(client);
        var (bToken, _, _) = await RegisterAsync(client);

        var mine = await CreateBoardAsync(client, aToken, "Mine");
        var theirs = await CreateBoardAsync(client, bToken, "Theirs");

        var list = await (await client.SendAsync(Authed(HttpMethod.Get, "/boards", aToken)))
            .Content.ReadFromJsonAsync<List<BoardDto>>(Json);

        list!.ShouldContain(b => b.Id == mine.Id);
        list!.ShouldNotContain(b => b.Id == theirs.Id);
    }

    [Fact]
    public async Task NonMember_GetBoard_IsForbidden()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (outsiderToken, _, _) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);

        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}", outsiderToken));
        res.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMissingBoard_IsNotFound()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);

        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{Guid.NewGuid()}", token));
        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Viewer_CannotManageMembers_ButAdminCan()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (viewerToken, _, viewerEmail) = await RegisterAsync(client);
        var (_, _, thirdEmail) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);
        (await AddMemberAsync(client, ownerToken, board.Id, viewerEmail, BoardRole.Viewer))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Viewer may not add members.
        (await AddMemberAsync(client, viewerToken, board.Id, thirdEmail, BoardRole.Viewer))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Owner (Admin) may.
        (await AddMemberAsync(client, ownerToken, board.Id, thirdEmail, BoardRole.Editor))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task PromotingViewerToAdmin_GrantsManagement()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (memberToken, memberId, memberEmail) = await RegisterAsync(client);
        var (_, _, thirdEmail) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, memberEmail, BoardRole.Viewer);

        // As Viewer: denied.
        (await AddMemberAsync(client, memberToken, board.Id, thirdEmail, BoardRole.Viewer))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Owner promotes to Admin.
        (await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/members/{memberId}", ownerToken,
            new { role = "Admin" }))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Same member now succeeds — role change takes effect immediately.
        (await AddMemberAsync(client, memberToken, board.Id, thirdEmail, BoardRole.Viewer))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OnlyOwner_CanDeleteBoard()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (adminToken, _, adminEmail) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);
        await AddMemberAsync(client, ownerToken, board.Id, adminEmail, BoardRole.Admin);

        // A non-owner Admin cannot delete.
        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}", adminToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The owner can.
        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}", ownerToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Owner_CannotBeDemotedOrRemoved()
    {
        var client = NewClient();
        var (ownerToken, ownerId, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);

        (await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/members/{ownerId}", ownerToken,
            new { role = "Viewer" }))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{board.Id}/members/{ownerId}", ownerToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddMember_UnknownEmail_IsNotFound()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);

        var res = await AddMemberAsync(client, ownerToken, board.Id, UniqueEmail(), BoardRole.Viewer);
        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ArchiveThenUnarchive_ByAdmin_KeepsBoardReadable()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, ownerToken);

        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/archive", ownerToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var archived = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}", ownerToken)))
            .Content.ReadFromJsonAsync<BoardDto>(Json);
        archived!.Archived.ShouldBeTrue();

        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/unarchive", ownerToken)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var restored = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}", ownerToken)))
            .Content.ReadFromJsonAsync<BoardDto>(Json);
        restored!.Archived.ShouldBeFalse();
    }

    /// <summary>
    /// The optional starter template. Seeding happens in the same SaveChanges as the board and its
    /// owner membership, so there is no interleaving that can leave a board half-seeded — the four
    /// lists either all exist or the board does not.
    /// </summary>
    [Fact]
    public async Task CreateBoard_WithTheTemplate_SeedsTheFourDefaultLists_InOrder()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);

        var res = await client.SendAsync(Authed(HttpMethod.Post, "/boards", token,
            new { name = "Seeded", seedDefaultLists = true }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        var board = (await res.Content.ReadFromJsonAsync<BoardDto>(Json))!;

        var lists = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/lists", token)))
            .Content.ReadFromJsonAsync<IReadOnlyList<ListDto>>(Json);

        // Order is the whole point: the lists come back ordered by rank, and a board that reads
        // "Done, Doing, Backlog, To Do" is not the template anyone asked for.
        lists!.Select(l => l.Name).ShouldBe(new[] { "Backlog", "To Do", "Doing", "Done" });
    }

    [Fact]
    public async Task CreateBoard_WithoutTheTemplate_SeedsNothing()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);

        // The flag defaults to false, so the plain create path — the one every other test and
        // client uses — must keep producing an empty board.
        var board = await CreateBoardAsync(client, token);

        var lists = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/lists", token)))
            .Content.ReadFromJsonAsync<IReadOnlyList<ListDto>>(Json);

        lists!.ShouldBeEmpty();
    }
}
