using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;
using Yanban.Application.Activities;
using Yanban.Application.Attachments;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Lists;

namespace Yanban.IntegrationTests;

/// <summary>
/// M6 — card attachments via presigned URLs. The API never touches the bytes: the
/// round-trip test uploads straight to MinIO with the presigned PUT and downloads with
/// the presigned GET, so it exercises the real object-storage path end-to-end. Those
/// two calls use a plain HttpClient — the WebApplicationFactory client is an in-memory
/// handler and cannot reach the external MinIO port.
/// </summary>
[Collection("api")]
public class AttachmentEndpointsTests
{
    private readonly YanbanApiFactory _factory;

    public AttachmentEndpointsTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"att_{Guid.NewGuid():N}@example.com";

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

    private async Task<CardDto> CreateCardAsync(HttpClient client, string token, Guid boardId, Guid listId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/lists/{listId}/cards", token, new { title = "Card" }));
        res.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<CardDto>(Json))!;
    }

    /// <summary>Owner + board + list + card, ready for attachment operations.</summary>
    private async Task<(string Token, Guid UserId, Guid BoardId, Guid CardId)> SeedCardAsync(HttpClient client)
    {
        var (token, userId, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);
        return (token, userId, board.Id, card.Id);
    }

    private async Task<UploadTicketDto> RequestUploadAsync(
        HttpClient client, string token, Guid boardId, Guid cardId, string fileName, string contentType, long sizeBytes)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{boardId}/cards/{cardId}/attachments", token, new { fileName, contentType, sizeBytes }));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<UploadTicketDto>(Json))!;
    }

    private static async Task PutBytesAsync(string uploadUrl, byte[] bytes, string contentType)
    {
        using var raw = new HttpClient();
        var content = new ByteArrayContent(bytes) { Headers = { ContentType = new MediaTypeHeaderValue(contentType) } };
        var res = await raw.PutAsync(uploadUrl, content);
        res.StatusCode.ShouldBe(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Attachment_FullPresignedRoundTrip_UploadsListsDownloadsDeletes()
    {
        var client = NewClient();
        var (token, userId, boardId, cardId) = await SeedCardAsync(client);
        var bytes = Encoding.UTF8.GetBytes("hello yanban — the bytes never touch the API");
        const string contentType = "text/plain";

        // 1) Ask for an upload slot, 2) PUT the bytes straight to MinIO.
        var ticket = await RequestUploadAsync(client, token, boardId, cardId, "note.txt", contentType, bytes.Length);
        ticket.Method.ShouldBe("PUT");
        await PutBytesAsync(ticket.UploadUrl, bytes, contentType);

        // 3) Confirm — the API HEADs the object and flips it to Ready.
        var complete = await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{boardId}/cards/{cardId}/attachments/{ticket.AttachmentId}/complete", token));
        complete.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await complete.Content.ReadFromJsonAsync<AttachmentDto>(Json))!;
        dto.FileName.ShouldBe("note.txt");
        dto.SizeBytes.ShouldBe(bytes.Length);
        dto.UploadedById.ShouldBe(userId);

        // 4) It shows up in the card's attachment list.
        var listed = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/cards/{cardId}/attachments", token)))
            .Content.ReadFromJsonAsync<List<AttachmentDto>>(Json);
        listed!.ShouldContain(a => a.Id == ticket.AttachmentId);

        // 5) Download via a fresh presigned GET and verify the bytes survived the trip.
        var download = (await (await client.SendAsync(Authed(HttpMethod.Get,
            $"/boards/{boardId}/cards/{cardId}/attachments/{ticket.AttachmentId}/download", token)))
            .Content.ReadFromJsonAsync<DownloadUrlDto>(Json))!;
        using var raw = new HttpClient();
        (await raw.GetByteArrayAsync(download.DownloadUrl)).ShouldBe(bytes);

        // 6) Delete removes it from the listing.
        (await client.SendAsync(Authed(HttpMethod.Delete, $"/boards/{boardId}/cards/{cardId}/attachments/{ticket.AttachmentId}", token)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var afterDelete = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/cards/{cardId}/attachments", token)))
            .Content.ReadFromJsonAsync<List<AttachmentDto>>(Json);
        afterDelete!.ShouldNotContain(a => a.Id == ticket.AttachmentId);

        // Completion and deletion each left an audit trail entry (M5 wiring).
        var feed = await (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/activity", token)))
            .Content.ReadFromJsonAsync<List<ActivityDto>>(Json);
        feed!.ShouldContain(a => a.EntityType == "Attachment" && a.Action == "Created");
        feed!.ShouldContain(a => a.EntityType == "Attachment" && a.Action == "Deleted");
    }

    [Fact]
    public async Task RequestUpload_ExceedingMaxSize_IsRejected()
    {
        var client = NewClient();
        var (token, _, boardId, cardId) = await SeedCardAsync(client);

        // Over the per-file cap, rejected before any URL is issued. 413 rather than 400 since M14:
        // the request is well-formed and the caller is entitled to make it — the payload is simply
        // too large, which is a thing a client can say something useful about.
        var res = await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/cards/{cardId}/attachments",
            token, new { fileName = "big.bin", contentType = "application/octet-stream", sizeBytes = 20_000_000 }));
        res.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Complete_WithoutUploadingTheBytes_IsRejected()
    {
        var client = NewClient();
        var (token, _, boardId, cardId) = await SeedCardAsync(client);

        // Ticket issued, but the client never PUTs the file — completion must fail
        // because the object isn't there (the API trusts storage, not the client's word).
        var ticket = await RequestUploadAsync(client, token, boardId, cardId, "ghost.txt", "text/plain", 10);
        (await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{boardId}/cards/{cardId}/attachments/{ticket.AttachmentId}/complete", token)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Complete_WhenUploadedSizeMismatchesDeclared_IsRejected()
    {
        var client = NewClient();
        var (token, _, boardId, cardId) = await SeedCardAsync(client);
        var bytes = Encoding.UTF8.GetBytes("actual bytes");
        const string contentType = "text/plain";

        // Declare a size that doesn't match what we actually upload.
        var ticket = await RequestUploadAsync(client, token, boardId, cardId, "lie.txt", contentType, bytes.Length + 100);
        await PutBytesAsync(ticket.UploadUrl, bytes, contentType);

        (await client.SendAsync(Authed(HttpMethod.Post,
            $"/boards/{boardId}/cards/{cardId}/attachments/{ticket.AttachmentId}/complete", token)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Attachment_AddressedUnderWrongCard_IsNotFound()
    {
        var client = NewClient();
        var (token, _, _) = await RegisterAsync(client);
        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var cardA = await CreateCardAsync(client, token, board.Id, list.Id);
        var cardB = await CreateCardAsync(client, token, board.Id, list.Id);

        var bytes = Encoding.UTF8.GetBytes("belongs to A");
        var ticket = await RequestUploadAsync(client, token, board.Id, cardA.Id, "a.txt", "text/plain", bytes.Length);
        await PutBytesAsync(ticket.UploadUrl, bytes, "text/plain");
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/cards/{cardA.Id}/attachments/{ticket.AttachmentId}/complete", token));

        // The attachment belongs to card A; addressing it under card B must not resolve.
        (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{board.Id}/cards/{cardB.Id}/attachments/{ticket.AttachmentId}/download", token)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attachments_RespectBoardPermissions()
    {
        var client = NewClient();
        var (ownerToken, _, boardId, cardId) = await SeedCardAsync(client);
        var (viewerToken, _, viewerEmail) = await RegisterAsync(client);
        var (outsiderToken, _, _) = await RegisterAsync(client);
        await AddMemberAsync(client, ownerToken, boardId, viewerEmail, "Viewer");

        // A Viewer has Read but not Write — cannot request an upload.
        (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/cards/{cardId}/attachments",
            viewerToken, new { fileName = "x.txt", contentType = "text/plain", sizeBytes = 5 })))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A non-member cannot even list.
        (await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/cards/{cardId}/attachments", outsiderToken)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
