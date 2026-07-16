using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using Yanban.Application.Auth;
using Yanban.Application.Boards;
using Yanban.Application.Cards;
using Yanban.Application.Common;
using Yanban.Application.Lists;
using Yanban.Application.Notifications;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Notifications;
using Yanban.Infrastructure.Persistence;

namespace Yanban.IntegrationTests;

/// <summary>
/// Email notifications on a transactional outbox.
///
/// <para>Two tests here carry the design. <see cref="Enqueue_DoesNotSave_UntilTheCallerDoes"/>
/// pins the contract that makes the outbox transactional at all: the writer only <c>Add</c>s, so a
/// mutation that rolls back takes its email with it. <see cref="TwoWorkers_ClaimDisjointBatches_AndSendNothingTwice"/>
/// pins the claim: it fails one way if the row lock is removed (the same mail goes out twice) and
/// a different way if <c>SKIP LOCKED</c> is downgraded to a plain <c>FOR UPDATE</c> (the second
/// worker blocks, wakes to find the rows spent, and does nothing at all).</para>
/// </summary>
[Collection("api")]
public class NotificationTests
{
    private readonly YanbanApiFactory _factory;

    public NotificationTests(YanbanApiFactory factory) => _factory = factory;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static string UniqueEmail() => $"ntf_{Guid.NewGuid():N}@example.com";

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

    /// <summary>A DbContext outside any request: the only way to see what actually committed.</summary>
    private async Task<T> WithDbAsync<T>(Func<YanbanDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<YanbanDbContext>());
    }

    private Task<List<OutboxMessage>> OutboxFor(Guid userId) =>
        WithDbAsync(db => db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.RecipientUserId == userId)
            .ToListAsync());

    // --- signup confirmation ------------------------------------------------

    [Fact]
    public async Task Register_QueuesAConfirmationEmail_AndLeavesTheAccountUnconfirmed()
    {
        var client = NewClient();
        var (token, userId, email) = await RegisterAsync(client);

        var messages = await OutboxFor(userId);
        var confirmation = messages.ShouldHaveSingleItem();
        confirmation.Type.ShouldBe(NotificationType.SignupConfirmation);
        confirmation.Status.ShouldBe(OutboxStatus.Pending);
        confirmation.RecipientEmail.ShouldBe(email);
        confirmation.BoardId.ShouldBeNull();

        var me = await (await client.SendAsync(Authed(HttpMethod.Get, "/me", token)))
            .Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("emailConfirmed").GetBoolean().ShouldBeFalse();
    }

    /// <summary>
    /// The soft policy, pinned. An unconfirmed account is nagged, not locked out; if this ever
    /// becomes a gate, it should be a deliberate decision that breaks this test loudly.
    /// </summary>
    [Fact]
    public async Task AnUnconfirmedUser_CanStillLogIn()
    {
        var client = NewClient();
        var email = UniqueEmail();
        await client.PostAsJsonAsync("/auth/register",
            new { email, password = "correct horse battery staple", displayName = "User" });

        var login = await client.PostAsJsonAsync("/auth/login",
            new { email, password = "correct horse battery staple" });

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfirmEmail_Works_AndTheTokenIsSingleUse()
    {
        var client = NewClient();
        var (token, userId, _) = await RegisterAsync(client);

        var raw = (await OutboxFor(userId))
            .Single(m => m.Type == NotificationType.SignupConfirmation)
            .Payload!;
        var confirmToken = JsonSerializer.Deserialize<SignupConfirmationPayload>(raw, Json)!.Token;

        var first = await client.PostAsJsonAsync("/auth/confirm-email", new { token = confirmToken });
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var me = await (await client.SendAsync(Authed(HttpMethod.Get, "/me", token)))
            .Content.ReadFromJsonAsync<JsonElement>();
        me.GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();

        // Replaying a spent link must not work; it is a bearer credential that has been used.
        var second = await client.PostAsJsonAsync("/auth/confirm-email", new { token = confirmToken });
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Resend runs authenticated as the recipient, so the actor check in
    /// <c>NotificationOutbox.EnqueueAsync</c> (never mail me about my own doing) would eat the
    /// confirmation unless <c>SignupConfirmation</c> is exempt. Symptom before the fix:
    /// <c>POST /auth/resend-confirmation</c> returns 204, the token row is written, and no mail is
    /// ever queued (the 204 lies). Registration queues one confirmation; a resend must queue a second.
    /// </summary>
    [Fact]
    public async Task ResendingConfirmation_QueuesAnotherMail_EvenThoughYouAreTheRecipient()
    {
        var client = NewClient();
        var (token, userId, email) = await RegisterAsync(client);

        // Registration queued the first one.
        (await OutboxFor(userId)).Count(m => m.Type == NotificationType.SignupConfirmation).ShouldBe(1);

        var resend = await client.SendAsync(Authed(HttpMethod.Post, "/auth/resend-confirmation", token));
        resend.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Without the SignupConfirmation exemption in the actor check this stays at 1: the mail is
        // dropped because sender == recipient, and the user waits for a link that never comes.
        var confirmations = (await OutboxFor(userId))
            .Where(m => m.Type == NotificationType.SignupConfirmation)
            .ToList();
        confirmations.Count.ShouldBe(2);
        confirmations.ShouldAllBe(m => m.RecipientEmail == email);
    }

    // --- who gets mailed, and who does not ----------------------------------

    [Fact]
    public async Task AssigningACard_MailsTheAssignee()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (_, memberId, memberEmail) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = memberEmail, role = "Editor" }));
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);

        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/assignee", ownerToken,
            new { assigneeId = memberId }));

        var mail = (await OutboxFor(memberId)).Single(m => m.Type == NotificationType.CardAssigned);
        mail.BoardId.ShouldBe(board.Id);
        mail.RecipientEmail.ShouldBe(memberEmail);
    }

    /// <summary>
    /// You are never mailed about your own doing. Without the actor check in
    /// <c>NotificationOutbox.EnqueueAsync</c>, assigning a card to yourself mails you about it,
    /// which is both useless and the fastest way to get a notification system muted.
    /// </summary>
    [Fact]
    public async Task AssigningACardToYourself_MailsNobody()
    {
        var client = NewClient();
        var (token, userId, _) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, token);
        var list = await CreateListAsync(client, token, board.Id);
        var card = await CreateCardAsync(client, token, board.Id, list.Id);

        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/assignee", token,
            new { assigneeId = userId }));

        (await OutboxFor(userId)).ShouldNotContain(m => m.Type == NotificationType.CardAssigned);
    }

    /// <summary>
    /// CommentCreated is the one type that is off by default: the settings panel shows it
    /// unchecked, and the code has to agree with the panel. Turning it on then produces the mail,
    /// which proves the default is what suppressed it rather than a missing enqueue.
    /// </summary>
    [Fact]
    public async Task Comments_AreOffByDefault_AndMailOnceEnabled()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (memberToken, memberId, _) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);
        var me = await (await client.SendAsync(Authed(HttpMethod.Get, "/me", memberToken)))
            .Content.ReadFromJsonAsync<JsonElement>();
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = me.GetProperty("email").GetString(), role = "Editor" }));

        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);
        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/assignee", ownerToken,
            new { assigneeId = memberId }));

        // The owner comments on the member's card. Default: silence.
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/cards/{card.Id}/comments", ownerToken,
            new { body = "First comment" }));

        (await OutboxFor(memberId)).ShouldNotContain(m => m.Type == NotificationType.CommentCreated);

        // The member opts in, on this board.
        var opt = await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/notification-preferences",
            memberToken, new { type = "CommentCreated", enabled = true }));
        opt.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/cards/{card.Id}/comments", ownerToken,
            new { body = "Second comment" }));

        (await OutboxFor(memberId)).ShouldContain(m => m.Type == NotificationType.CommentCreated);
    }

    [Fact]
    public async Task TurningAPreferenceOff_SilencesIt()
    {
        var client = NewClient();
        var (ownerToken, _, _) = await RegisterAsync(client);
        var (memberToken, memberId, memberEmail) = await RegisterAsync(client);

        var board = await CreateBoardAsync(client, ownerToken);
        await client.SendAsync(Authed(HttpMethod.Post, $"/boards/{board.Id}/members", ownerToken,
            new { email = memberEmail, role = "Editor" }));
        var list = await CreateListAsync(client, ownerToken, board.Id);
        var card = await CreateCardAsync(client, ownerToken, board.Id, list.Id);

        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/notification-preferences",
            memberToken, new { type = "CardAssigned", enabled = false }));

        await client.SendAsync(Authed(HttpMethod.Put, $"/boards/{board.Id}/cards/{card.Id}/assignee", ownerToken,
            new { assigneeId = memberId }));

        (await OutboxFor(memberId)).ShouldNotContain(m => m.Type == NotificationType.CardAssigned);
    }

    // --- the contract that makes it transactional ---------------------------

    /// <summary>
    /// The whole design in one assertion: <c>EnqueueAsync</c> only <c>Add</c>s. Nothing is in the
    /// table until the caller saves, which is what makes a rolled-back mutation take its email
    /// down with it. Put a <c>SaveChanges</c> inside the outbox and this goes red immediately.
    /// </summary>
    [Fact]
    public async Task Enqueue_DoesNotSave_UntilTheCallerDoes()
    {
        var client = NewClient();
        var (_, userId, _) = await RegisterAsync(client);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
        var outbox = new NotificationOutbox(
            db,
            new NoCurrentUser(),
            new NotificationPreferenceService(db));

        var before = await OutboxFor(userId);

        await outbox.EnqueueAsync(
            NotificationType.CardAssigned,
            userId,
            boardId: null,
            new CardNotificationPayload("Someone", "Board", "Card", Guid.NewGuid()),
            CancellationToken.None);

        // Staged in the unit of work...
        db.ChangeTracker.Entries<OutboxMessage>()
            .Count(e => e.State == EntityState.Added)
            .ShouldBe(1);

        // ...and, crucially, not in the database.
        (await OutboxFor(userId)).Count.ShouldBe(before.Count);

        await db.SaveChangesAsync();

        (await OutboxFor(userId)).Count.ShouldBe(before.Count + 1);
    }

    // --- the claim ----------------------------------------------------------

    /// <summary>
    /// Ten pending messages, two workers, a batch size of five, and a sender slow enough that the
    /// two passes genuinely overlap.
    ///
    /// <para>The row lock is what stops a duplicate. Take <c>FOR UPDATE</c> out of the claim and
    /// both workers read the same five rows from the same snapshot: the same mail is sent twice,
    /// and this goes red on the duplicate check.</para>
    ///
    /// <para>Note what this does not prove. A plain <c>FOR UPDATE</c>, no <c>SKIP LOCKED</c>, also
    /// passes: the second worker simply blocks, wakes to find those rows spent, and scans on to the
    /// other five. Same outcome, serialized. What <c>SKIP LOCKED</c> actually buys is that it does
    /// not block at all, which is a different property and gets its own test below.</para>
    /// </summary>
    [Fact]
    public async Task TwoWorkers_NeverSendTheSameMessageTwice()
    {
        var client = NewClient();
        var (_, userId, email) = await RegisterAsync(client);

        // The claim query is global: it asks "what is Pending?", not "what is Pending for this
        // test". Every other test in this collection leaves its own signup confirmations queued, and
        // a worker would happily claim those instead of ours, making the batch arithmetic below
        // meaningless. So drain the queue to empty first, and only then seed a known ten.
        // (Tests in a collection do not run in parallel, so nothing refills it behind us.)
        await DrainAsync();

        var mine = new List<Guid>();
        using (var seed = _factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<YanbanDbContext>();
            for (var i = 0; i < 10; i++)
            {
                var id = Guid.NewGuid();
                mine.Add(id);
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = id,
                    Type = NotificationType.CardAssigned,
                    RecipientUserId = userId,
                    RecipientEmail = email,
                    BoardId = Guid.NewGuid(),
                    Payload = JsonSerializer.Serialize(
                        new CardNotificationPayload("Actor", "Board", $"Card {i}", Guid.NewGuid()),
                        NotificationOutbox.JsonOptions),
                    Status = OutboxStatus.Pending,
                    NextAttemptAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        var sent = new ConcurrentBag<string>();
        var options = Options.Create(new EmailOptions { BatchSize = 5 });

        // Each worker gets its own scope, and so its own DbContext and its own connection; two
        // workers sharing a connection would serialize in the client and prove nothing.
        async Task<int> RunWorker()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
            var processor = new OutboxProcessor(
                db,
                new SlowRecordingSender(sent, TimeSpan.FromMilliseconds(400)),
                options,
                NullLogger<OutboxProcessor>.Instance);
            return await processor.ProcessBatchAsync(CancellationToken.None);
        }

        var claims = await Task.WhenAll(RunWorker(), RunWorker());

        // Nothing was sent twice; the lock did its job. Without it, both workers claim the same
        // five and this is 15 sends of 10 distinct subjects.
        sent.Count.ShouldBe(10);
        sent.Distinct().Count().ShouldBe(10);

        // Between them they took the whole queue, and split it.
        claims.Sum().ShouldBe(10);

        var after = await WithDbAsync(db => db.OutboxMessages
            .AsNoTracking()
            .Where(m => mine.Contains(m.Id))
            .ToListAsync());

        after.Count.ShouldBe(10);
        after.ShouldAllBe(m => m.Status == OutboxStatus.Sent);
        // One send apiece, and the spent payload is gone: a sent confirmation must not leave a
        // working token sitting in the table.
        after.ShouldAllBe(m => m.Attempts == 1);
        after.ShouldAllBe(m => m.Payload == null);
    }

    /// <summary>
    /// What <c>SKIP LOCKED</c> is actually for, stated as an ordering rather than a stopwatch.
    ///
    /// <para>Worker A claims five messages and sits inside a very slow send, holding their row locks
    /// with its transaction still open. Worker B then runs. It must step over A's five, take the
    /// other five, and finish while A is still going.</para>
    ///
    /// <para>Downgrade the claim to a plain <c>FOR UPDATE</c> and B blocks on A's locks until A
    /// commits, so B cannot possibly finish first: <c>slowWorker.IsCompleted</c> is true by the time
    /// B returns, and this goes red. No timing threshold to tune: the assertion is "B beat A", and
    /// A is deliberately glacial.</para>
    /// </summary>
    [Fact]
    public async Task AWorker_DoesNotBlock_BehindAnotherWorkersBatch()
    {
        var client = NewClient();
        var (_, userId, email) = await RegisterAsync(client);
        await DrainAsync();

        using (var seed = _factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<YanbanDbContext>();
            for (var i = 0; i < 10; i++)
            {
                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    Type = NotificationType.CardAssigned,
                    RecipientUserId = userId,
                    RecipientEmail = email,
                    BoardId = Guid.NewGuid(),
                    Payload = JsonSerializer.Serialize(
                        new CardNotificationPayload("Actor", "Board", $"Card {i}", Guid.NewGuid()),
                        NotificationOutbox.JsonOptions),
                    Status = OutboxStatus.Pending,
                    NextAttemptAt = DateTimeOffset.UtcNow,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        var options = Options.Create(new EmailOptions { BatchSize = 5 });

        async Task<int> RunWorker(TimeSpan sendDelay)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
            var processor = new OutboxProcessor(
                db,
                new SlowRecordingSender(new ConcurrentBag<string>(), sendDelay),
                options,
                NullLogger<OutboxProcessor>.Instance);
            return await processor.ProcessBatchAsync(CancellationToken.None);
        }

        // A: claims five and crawls: five sends at two seconds each, locks held throughout.
        var slowWorker = RunWorker(TimeSpan.FromSeconds(2));

        // Give A time to have taken its batch. (Generous: we are proving B does not block, so the
        // only thing this delay can do is make the test harder to pass.)
        await Task.Delay(TimeSpan.FromSeconds(1));

        // B: instant sender. If the claim blocks, this cannot return until A has committed.
        var claimed = await RunWorker(TimeSpan.Zero);

        claimed.ShouldBe(5);
        slowWorker.IsCompleted.ShouldBeFalse("B blocked behind A's locked rows — the claim is not skipping them");

        (await slowWorker).ShouldBe(5);
    }

    [Fact]
    public async Task ASenderThatFails_BacksOffAndKeepsTheMessage()
    {
        var client = NewClient();
        var (_, userId, email) = await RegisterAsync(client);

        // Same reason as the race above: other tests' queued mail is older than ours, and would
        // fill the batch ahead of it.
        await DrainAsync();

        var id = Guid.NewGuid();
        using (var seed = _factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<YanbanDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = id,
                Type = NotificationType.CardAssigned,
                RecipientUserId = userId,
                RecipientEmail = email,
                BoardId = Guid.NewGuid(),
                Payload = JsonSerializer.Serialize(
                    new CardNotificationPayload("Actor", "Board", "Card", Guid.NewGuid()),
                    NotificationOutbox.JsonOptions),
                Status = OutboxStatus.Pending,
                NextAttemptAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
            var processor = new OutboxProcessor(
                db,
                new ThrowingSender(),
                Options.Create(new EmailOptions()),
                NullLogger<OutboxProcessor>.Instance);
            await processor.ProcessBatchAsync(CancellationToken.None);
        }

        var message = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().FirstAsync(m => m.Id == id));

        // Still ours to send, just not yet: a failed send must not lose the message.
        message.Status.ShouldBe(OutboxStatus.Pending);
        message.Attempts.ShouldBe(1);
        message.LastError.ShouldNotBeNull();
        message.NextAttemptAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        message.Payload.ShouldNotBeNull();
    }

    /// <summary>Sends every queued message into the void, until there is nothing left to claim.</summary>
    private async Task DrainAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<YanbanDbContext>();
        var processor = new OutboxProcessor(
            db,
            new NullSender(),
            Options.Create(new EmailOptions { BatchSize = 200 }),
            NullLogger<OutboxProcessor>.Instance);

        while (await processor.ProcessBatchAsync(CancellationToken.None) > 0) { }
    }

    private sealed class NullSender : IEmailSender
    {
        public Task SendAsync(OutgoingEmail email, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SlowRecordingSender : IEmailSender
    {
        private readonly ConcurrentBag<string> _sent;
        private readonly TimeSpan _delay;

        public SlowRecordingSender(ConcurrentBag<string> sent, TimeSpan delay)
        {
            _sent = sent;
            _delay = delay;
        }

        public async Task SendAsync(OutgoingEmail email, CancellationToken ct)
        {
            // Long enough that the two claims genuinely overlap; without this the first worker
            // finishes before the second starts and the race is never run.
            await Task.Delay(_delay, ct);
            _sent.Add(email.Subject);
        }
    }

    private sealed class ThrowingSender : IEmailSender
    {
        public Task SendAsync(OutgoingEmail email, CancellationToken ct) =>
            throw new InvalidOperationException("The relay is down.");
    }

    /// <summary>No authenticated request behind this call, so nothing is ever "your own doing".</summary>
    private sealed class NoCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
    }
}
