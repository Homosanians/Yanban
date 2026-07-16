using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Lists;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.IntegrationTests;

/// <summary>
/// Configurable templates, and the storage-leak fix.
///
/// <para>The load-bearing test is <see cref="DeletingABoard_EnqueuesEveryAttachmentsObject"/>: it
/// deletes a board and asserts the object_deletions queue holds the key of an attachment two
/// levels down (board, list, card, attachment). That path is a Postgres cascade the application
/// never sees, so only a database trigger can catch it.</para>
/// </summary>
[Collection("api")]
public class BoardTemplateAndGcTests
{
    private readonly YanbanApiFactory _factory;

    public BoardTemplateAndGcTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

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

    private async Task<string> RegisterAsync(HttpClient client)
    {
        var reg = await client.PostAsJsonAsync("/auth/register", new
        {
            email = $"tpl_{Guid.NewGuid():N}@example.com",
            password = "correct horse battery staple",
            displayName = "Templater"
        });
        return (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;
    }

    private async Task<T> WithDbAsync<T>(Func<YanbanDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<YanbanDbContext>());
    }

    private async Task<List<string>> ListNamesAsync(HttpClient client, string token, Guid boardId)
    {
        var lists = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/lists", token)))
            .Content.ReadFromJsonAsync<List<ListDto>>(Json);
        return lists!.Select(l => l.Name).ToList();
    }

    // --- templates ----------------------------------------------------------

    [Fact]
    public async Task ListTemplates_OffersSimpleAndDevFlow()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);

        var res = await client.SendAsync(Authed(HttpMethod.Get, "/board-templates", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var templates = (await res.Content.ReadFromJsonAsync<List<BoardTemplateDto>>(Json))!;

        templates.Select(t => t.Id).ShouldBe(new[] { "simple", "dev-flow" });
        templates.Single(t => t.Id == "dev-flow").Lists
            .ShouldBe(new[] { "Backlog", "Ready for Dev", "In Progress", "Code Review", "QA", "Done" });
    }

    [Fact]
    public async Task CreatingWithTheDevFlowTemplate_SeedsThoseListsInOrder()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);

        var board = (await (await client.SendAsync(Authed(HttpMethod.Post, "/boards", token,
            new { name = "Project", template = "dev-flow" }))).Content.ReadFromJsonAsync<BoardDto>(Json))!;

        (await ListNamesAsync(client, token, board.Id))
            .ShouldBe(new[] { "Backlog", "Ready for Dev", "In Progress", "Code Review", "QA", "Done" });
    }

    /// <summary>The legacy boolean still works: an old client that only knows it gets the simple template.</summary>
    [Fact]
    public async Task TheLegacySeedFlag_StillMeansTheSimpleTemplate()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);

        var board = (await (await client.SendAsync(Authed(HttpMethod.Post, "/boards", token,
            new { name = "Legacy", seedDefaultLists = true }))).Content.ReadFromJsonAsync<BoardDto>(Json))!;

        (await ListNamesAsync(client, token, board.Id))
            .ShouldBe(new[] { "Backlog", "To Do", "Doing", "Done" });
    }

    [Fact]
    public async Task AnUnknownTemplate_IsRejected()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);

        var res = await client.SendAsync(Authed(HttpMethod.Post, "/boards", token,
            new { name = "Nope", template = "kanban-deluxe" }));

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task NoTemplate_LeavesAnEmptyBoard()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);

        var board = (await (await client.SendAsync(Authed(HttpMethod.Post, "/boards", token,
            new { name = "Blank" }))).Content.ReadFromJsonAsync<BoardDto>(Json))!;

        (await ListNamesAsync(client, token, board.Id)).ShouldBeEmpty();
    }

    // --- the storage leak ---------------------------------------------------

    /// <summary>
    /// Seeds a Ready attachment, deletes the whole board, and asserts the trigger enqueued the
    /// object, reaching it through a two-level cascade (board, list, card, attachment) that no
    /// application code touches. Remove the trigger and the queue stays empty and the object leaks.
    /// </summary>
    [Fact]
    public async Task DeletingABoard_EnqueuesEveryAttachmentsObject()
    {
        var (boardId, storageKey) = await SeedReadyAttachmentAsync();

        // Delete the board directly in the database, a cascade, exactly what "deleted via db" means.
        await WithDbAsync(async db =>
        {
            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync();
            return 0;
        });

        var queued = await WithDbAsync(db => db.ObjectDeletions
            .AsNoTracking()
            .Where(o => o.StorageKey == storageKey)
            .ToListAsync());

        queued.ShouldHaveSingleItem().DeletedAt.ShouldBeNull();
    }

    /// <summary>Deleting a single card enqueues its attachment's object too: same trigger, shorter cascade.</summary>
    [Fact]
    public async Task DeletingACard_EnqueuesItsAttachmentsObject()
    {
        var (_, storageKey, cardId) = await SeedReadyAttachmentWithCardAsync();

        await WithDbAsync(async db =>
        {
            await db.Cards.Where(c => c.Id == cardId).ExecuteDeleteAsync();
            return 0;
        });

        (await WithDbAsync(db => db.ObjectDeletions.AsNoTracking()
            .CountAsync(o => o.StorageKey == storageKey))).ShouldBe(1);
    }

    private async Task<(Guid BoardId, string StorageKey)> SeedReadyAttachmentAsync()
    {
        var (boardId, storageKey, _) = await SeedReadyAttachmentWithCardAsync();
        return (boardId, storageKey);
    }

    /// <summary>
    /// Inserts a Ready attachment straight through the DbContext; this test is about the trigger,
    /// not the upload flow, and MinIO never needs to hold real bytes for the DB rows to exist.
    /// </summary>
    private async Task<(Guid BoardId, string StorageKey, Guid CardId)> SeedReadyAttachmentWithCardAsync()
    {
        var client = NewClient();
        var token = await RegisterAsync(client);

        var board = (await (await client.SendAsync(Authed(HttpMethod.Post, "/boards", token,
            new { name = "Files" }))).Content.ReadFromJsonAsync<BoardDto>(Json))!;
        var list = (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists", token,
            new { name = "L" }))).Content.ReadFromJsonAsync<ListDto>(Json))!;
        var card = (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists/{list.Id}/cards", token,
            new { title = "C" }))).Content.ReadFromJsonAsync<CardDto>(Json))!;

        var storageKey = $"attachments/{Guid.NewGuid()}";
        await WithDbAsync(async db =>
        {
            var uploaderId = await db.BoardMembers
                .Where(m => m.BoardId == board.Id)
                .Select(m => m.UserId)
                .FirstAsync();

            db.Attachments.Add(new Attachment
            {
                Id = Guid.NewGuid(),
                CardId = card.Id,
                FileName = "f.bin",
                ContentType = "application/octet-stream",
                SizeBytes = 1234,
                StorageKey = storageKey,
                Status = AttachmentStatus.Ready,
                UploadedById = uploaderId,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return 0;
        });

        return (board.Id, storageKey, card.Id);
    }
}
