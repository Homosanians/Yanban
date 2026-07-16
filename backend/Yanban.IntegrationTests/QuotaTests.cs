using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;
using Yanban.Application.Attachments;
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Lists;

namespace Yanban.IntegrationTests;

/// <summary>
/// Upload quotas. The test fixture shrinks them to 1 MiB per file and 5 MiB per board
/// (see <see cref="YanbanApiFactory"/>); production is 2 GiB and 50 GiB.
///
/// <para><see cref="ConcurrentUploads_CannotBothSlipUnderTheSameLimit"/> is the one that matters.
/// The quota check is a read followed by a write, and without the board lock two callers each read
/// "there is room for one more" and each conclude they are the one. It fails without the lock.</para>
/// </summary>
[Collection("api")]
public class QuotaTests
{
    private const long MaxFileBytes = 1024 * 1024;      // 1 MiB, matches the fixture
    private const long MaxBoardBytes = 5 * 1024 * 1024; // 5 MiB

    private readonly YanbanApiFactory _factory;

    public QuotaTests(YanbanApiFactory factory) => _factory = factory;

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

    private async Task<(string Token, Guid BoardId, Guid CardId)> SeedCardAsync(HttpClient client)
    {
        var reg = await client.PostAsJsonAsync("/auth/register", new
        {
            email = $"quo_{Guid.NewGuid():N}@example.com",
            password = "correct horse battery staple",
            displayName = "Quota"
        });
        var token = (await reg.Content.ReadFromJsonAsync<AccessTokenResponse>())!.AccessToken;

        var board = (await (await client.SendAsync(Authed(HttpMethod.Post, "/boards", token, new { name = "Storage" })))
            .Content.ReadFromJsonAsync<BoardDto>(Json))!;
        var list = (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists", token, new { name = "L" })))
            .Content.ReadFromJsonAsync<ListDto>(Json))!;
        var card = (await (await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/lists/{list.Id}/cards", token, new { title = "C" })))
            .Content.ReadFromJsonAsync<CardDto>(Json))!;

        return (token, board.Id, card.Id);
    }

    private Task<HttpResponseMessage> RequestUploadAsync(
        HttpClient client, string token, Guid boardId, Guid cardId, long sizeBytes) =>
        client.SendAsync(Authed(HttpMethod.Post, $"/boards/{boardId}/cards/{cardId}/attachments", token,
            new { fileName = "f.bin", contentType = "application/octet-stream", sizeBytes }));

    private async Task<BoardUsageDto> UsageAsync(HttpClient client, string token, Guid boardId)
    {
        var res = await client.SendAsync(Authed(HttpMethod.Get, $"/boards/{boardId}/usage", token));
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<BoardUsageDto>(Json))!;
    }

    [Fact]
    public async Task AFileOverThePerFileLimit_IsRejectedWith413()
    {
        var client = NewClient();
        var (token, boardId, cardId) = await SeedCardAsync(client);

        var res = await RequestUploadAsync(client, token, boardId, cardId, MaxFileBytes + 1);

        res.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        // The message has to be usable in a toast, so it must say the numbers out loud.
        (await res.Content.ReadAsStringAsync()).ShouldContain("per file");
    }

    [Fact]
    public async Task FillingTheBoard_ThenAskingForMore_IsRejectedWith413()
    {
        var client = NewClient();
        var (token, boardId, cardId) = await SeedCardAsync(client);

        // Five tickets of 1 MiB fills a 5 MiB board exactly. They are reservations: no bytes are
        // ever uploaded here, which is the point: the ticket alone consumes the quota.
        for (var i = 0; i < 5; i++)
            (await RequestUploadAsync(client, token, boardId, cardId, MaxFileBytes)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var res = await RequestUploadAsync(client, token, boardId, cardId, 1);

        res.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        (await res.Content.ReadAsStringAsync()).ShouldContain("left of its");
    }

    /// <summary>
    /// The race the board lock exists for.
    ///
    /// <para>The board has room for exactly one more 1 MiB file. Eight callers ask at once. Exactly
    /// one may win; without the <c>FOR UPDATE</c> on the board row, all eight read the same
    /// "4 MiB used" and all eight are told yes, putting the board 3 MiB over a limit it is supposed
    /// to be incapable of exceeding.</para>
    /// </summary>
    [Fact]
    public async Task ConcurrentUploads_CannotBothSlipUnderTheSameLimit()
    {
        var client = NewClient();
        var (token, boardId, cardId) = await SeedCardAsync(client);

        // 4 of the 5 MiB spoken for: room for exactly one more.
        for (var i = 0; i < 4; i++)
            (await RequestUploadAsync(client, token, boardId, cardId, MaxFileBytes)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Each racer gets its own HttpClient: sharing one would let connection pooling serialize
        // them in the client and the race would never actually run.
        var racers = Enumerable.Range(0, 8)
            .Select(_ => RequestUploadAsync(NewClient(), token, boardId, cardId, MaxFileBytes))
            .ToArray();

        var results = await Task.WhenAll(racers);

        var accepted = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        var refused = results.Count(r => r.StatusCode == HttpStatusCode.RequestEntityTooLarge);

        accepted.ShouldBe(1, "the board had room for exactly one more file");
        refused.ShouldBe(7);

        // And the board is exactly full, never over.
        var usage = await UsageAsync(client, token, boardId);
        usage.MaxBoardBytes.ShouldBe(MaxBoardBytes);
    }

    [Fact]
    public async Task Usage_ReportsWhatTheBoardIsActuallyHolding()
    {
        var client = NewClient();
        var (token, boardId, cardId) = await SeedCardAsync(client);

        var before = await UsageAsync(client, token, boardId);
        before.UsedBytes.ShouldBe(0);
        before.FileCount.ShouldBe(0);
        before.MaxFileBytes.ShouldBe(MaxFileBytes);
        before.MaxBoardBytes.ShouldBe(MaxBoardBytes);

        // A ticket alone must NOT move the bar: it reserves quota, but nothing has been stored, and
        // showing a stranger's half-finished upload as used space would be baffling to look at.
        (await RequestUploadAsync(client, token, boardId, cardId, 4096)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var pending = await UsageAsync(client, token, boardId);
        pending.UsedBytes.ShouldBe(0);
        pending.FileCount.ShouldBe(0);
    }
}
