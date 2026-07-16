using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Shouldly;
using Yanban.Application.Common;
using Yanban.Infrastructure.Activities;

namespace Yanban.IntegrationTests;

/// <summary>
/// The LISTEN/NOTIFY doorbell behind the realtime tailer: the trigger fires on an activity
/// insert, the listener wakes on a notification, and the backstop still fires when no
/// notification arrives. End-to-end delivery over the hub is covered by RealtimeHubTests.
/// </summary>
[Collection("api")]
public class ActivityNotificationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly YanbanApiFactory _factory;

    public ActivityNotificationTests(YanbanApiFactory factory) => _factory = factory;

    [Fact]
    public async Task InsertingAnActivityRow_FiresANotification()
    {
        // Force host startup so migrations (including the notify trigger) are applied; this test
        // otherwise only touches raw connections and could run before the schema exists.
        using var _ = _factory.CreateClient();

        await using var listen = new NpgsqlConnection(_factory.ConnectionString);
        await listen.OpenAsync();
        await using (var cmd = new NpgsqlCommand($"LISTEN {PostgresActivityListener.Channel}", listen))
            await cmd.ExecuteNonQueryAsync();

        // Insert on a separate connection; the commit is what fires the trigger.
        await using (var writer = new NpgsqlConnection(_factory.ConnectionString))
        {
            await writer.OpenAsync();
            await InsertActivityAsync(writer);
        }

        // WaitAsync reads the queued notification and returns true. False means the trigger
        // never published on the channel.
        var received = await listen.WaitAsync(Patience);
        received.ShouldBeTrue();
    }

    [Fact]
    public async Task Listener_WakesOnNotification()
    {
        await using var listener = BuildListener(backstopMs: 60_000);

        // First call connects and returns immediately (catch-up semantics).
        await listener.WaitForActivityAsync(CancellationToken.None);

        // The second call blocks until a notification. With a 60s backstop, returning quickly
        // can only be the doorbell.
        var wake = listener.WaitForActivityAsync(CancellationToken.None);
        await NotifyAsync();

        await wake.WaitAsync(Patience); // throws TimeoutException if the doorbell never fired
        wake.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task Listener_BackstopFires_WithoutAnyNotification()
    {
        await using var listener = BuildListener(backstopMs: 500);

        await listener.WaitForActivityAsync(CancellationToken.None); // connect and catch up

        // No notification is sent. The wait must still return on the backstop, well inside the
        // 5s allowance but after the 500ms backstop.
        await listener.WaitForActivityAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private PostgresActivityListener BuildListener(int backstopMs)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _factory.ConnectionString
            })
            .Build();

        var options = Options.Create(new RealtimeOptions { BackstopPollMs = backstopMs });
        return new PostgresActivityListener(config, options, NullLogger<PostgresActivityListener>.Instance);
    }

    private async Task NotifyAsync()
    {
        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"NOTIFY {PostgresActivityListener.Channel}", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    // A minimal row. The actor is not a real user, so the tailer's join drops it: this exercises
    // the trigger, not delivery. Sequence is identity, search_vector is generated, both omitted.
    private static async Task InsertActivityAsync(NpgsqlConnection conn)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO activity_logs (id, board_id, actor_id, action, entity_type, entity_id, created_at)
            VALUES (@id, @board, @actor, 'Created', 'Card', @entity, now())
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("board", Guid.NewGuid());
        cmd.Parameters.AddWithValue("actor", Guid.NewGuid());
        cmd.Parameters.AddWithValue("entity", Guid.NewGuid());
        await cmd.ExecuteNonQueryAsync();
    }
}
