using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Yanban.Application.Abstractions;
using Yanban.Application.Attachments;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Common;
using Yanban.Application.Lists;
using Yanban.Application.Cards;
using Yanban.Infrastructure.Persistence;
using Yanban.Infrastructure.Storage;

namespace Yanban.IntegrationTests;

/// <summary>
/// M15 — the janitor that drains the object-deletion queue to storage.
///
/// <para>This is a <b>separate</b> test from the trigger tests, deliberately. The worker is not
/// hosted by <see cref="YanbanApiFactory"/>, so there is no in-process path from "delete the row"
/// to "object gone from MinIO" — pretending otherwise would be a vacuous test. Instead it builds a
/// <see cref="StorageJanitor"/> against the same Testcontainers Postgres + MinIO the API uses, and
/// drives it directly. The end-to-end claim is: a real object, uploaded to real storage, is
/// actually deleted from it.</para>
/// </summary>
[Collection("api")]
public class StorageJanitorTests
{
    private readonly YanbanApiFactory _factory;

    public StorageJanitorTests(YanbanApiFactory factory) => _factory = factory;

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

    private StorageJanitor NewJanitor(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();
        // EmailOptions is only registered in the worker host; the API DI does not have it. The
        // janitor only reads BatchSize from it, so a default instance is exactly right here.
        return new StorageJanitor(db, storage, Options.Create(new EmailOptions()), NullLogger<StorageJanitor>.Instance);
    }

    /// <summary>
    /// Uploads a real object through the presigned flow, deletes the attachment (firing the trigger),
    /// runs the janitor, and asserts the bytes are actually gone from MinIO.
    /// </summary>
    [Fact]
    public async Task Janitor_DeletesAnEnqueuedObject_FromStorage()
    {
        var client = NewClient();

        var reg = await client.PostAsJsonAsync("/auth/register", new
        {
            email = $"jan_{Guid.NewGuid():N}@example.com",
            password = "correct horse battery staple",
            displayName = "Janitor"
        });
        var token = (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;

        var board = (await (await client.SendAsync(Authed(HttpMethod.Post, "/boards", token, new { name = "B" })))
            .Content.ReadFromJsonAsync<BoardDto>(Json))!;
        var list = (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists", token, new { name = "L" })))
            .Content.ReadFromJsonAsync<ListDto>(Json))!;
        var card = (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists/{list.Id}/cards", token, new { title = "C" })))
            .Content.ReadFromJsonAsync<CardDto>(Json))!;

        var bytes = Encoding.UTF8.GetBytes("delete me");
        var ticket = (await (await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{board.Id}/cards/{card.Id}/attachments", token,
            new { fileName = "gone.txt", contentType = "text/plain", sizeBytes = bytes.Length })))
            .Content.ReadFromJsonAsync<UploadTicketDto>(Json))!;

        using (var raw = new HttpClient())
        {
            var content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } };
            (await raw.PutAsync(ticket.UploadUrl, content)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{board.Id}/cards/{card.Id}/attachments/{ticket.AttachmentId}/complete", token));

        // The key the object lives under, straight from the row.
        string storageKey;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

            storageKey = await db.Attachments.Where(a => a.Id == ticket.AttachmentId).Select(a => a.StorageKey).FirstAsync();
            // Sanity: the bytes really are in MinIO before we delete anything.
            (await storage.TryGetObjectSizeAsync(storageKey, CancellationToken.None)).ShouldBe(bytes.Length);
        }

        // Delete the attachment via the API — no S3 call happens here anymore (ADR-20); the trigger
        // just enqueues the object.
        (await client.SendAsync(Authed(HttpMethod.Delete,
            $"/boards/{board.Id}/cards/{card.Id}/attachments/{ticket.AttachmentId}", token)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
            (await db.ObjectDeletions.CountAsync(o => o.StorageKey == storageKey && o.DeletedAt == null))
                .ShouldBe(1, "the delete should have enqueued the object");

            // Run the janitor.
            var claimed = await NewJanitor(scope).DrainDeletionsAsync(CancellationToken.None);
            claimed.ShouldBeGreaterThanOrEqualTo(1);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorage>();

            // The object is gone from storage...
            (await storage.TryGetObjectSizeAsync(storageKey, CancellationToken.None)).ShouldBeNull();
            // ...and its queue row is marked done, so no worker touches it again.
            (await db.ObjectDeletions.Where(o => o.StorageKey == storageKey).Select(o => o.DeletedAt).FirstAsync())
                .ShouldNotBeNull();
        }
    }

    /// <summary>
    /// Deleting an object that is already gone is a no-op — which is what makes at-least-once safe.
    /// A janitor row whose object never existed still gets marked done rather than retried forever.
    /// </summary>
    [Fact]
    public async Task Janitor_MarkingAnAlreadyGoneObject_IsNotAnError()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();

        db.ObjectDeletions.Add(new Domain.Entities.ObjectDeletion
        {
            StorageKey = $"attachments/{Guid.NewGuid()}", // never uploaded
            EnqueuedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await NewJanitor(scope).DrainDeletionsAsync(CancellationToken.None);

        (await db.ObjectDeletions.CountAsync(o => o.DeletedAt == null)).ShouldBe(0);
    }
}
