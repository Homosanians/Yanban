using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;

namespace Yanban.Infrastructure.Activities;

/// <summary>
/// Postgres LISTEN/NOTIFY doorbell for the realtime tailer. Holds one dedicated connection
/// listening on <see cref="Channel"/>; a trigger on activity_logs fires pg_notify when a row
/// is inserted, delivered on commit. The notification carries no payload: it is a bare wake
/// signal, and the tailer still reads the rows from the durable log via its cursor.
/// </summary>
public sealed class PostgresActivityListener : IActivityListener, IAsyncDisposable
{
    /// <summary>
    /// The channel the notify trigger publishes on. Must match the literal frozen into the
    /// AddActivityNotifyTrigger migration. Changing one without the other silently stops the
    /// doorbell; only the backstop poll would still deliver, and slowly.
    /// </summary>
    public const string Channel = "yanban_activity";

    private readonly string _connectionString;
    private readonly TimeSpan _backstop;
    private readonly ILogger<PostgresActivityListener> _logger;

    private NpgsqlConnection? _connection;

    public PostgresActivityListener(
        IConfiguration configuration,
        IOptions<RealtimeOptions> options,
        ILogger<PostgresActivityListener> logger)
    {
        _connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is not configured.");
        _backstop = TimeSpan.FromMilliseconds(options.Value.BackstopPollMs);
        _logger = logger;
    }

    public async Task WaitForActivityAsync(CancellationToken ct)
    {
        // (Re)open the listening connection when needed. A fresh LISTEN means notifications may
        // have fired while we were disconnected, so return straight away and let the caller do a
        // catch-up read. Nothing is lost: the log still holds every row past the cursor.
        if (_connection is not { State: ConnectionState.Open })
        {
            await ConnectAsync(ct);
            return;
        }

        try
        {
            // Returns on the first notification, or when the backstop elapses. The caller polls
            // the outbox either way, so the result is not needed.
            await _connection.WaitAsync(_backstop, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Activity listener connection dropped; will reconnect.");
            await DisposeConnectionAsync();
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using (var cmd = new NpgsqlCommand($"LISTEN {Channel}", connection))
                await cmd.ExecuteNonQueryAsync(ct);
            _connection = connection;
            _logger.LogDebug("Activity listener listening on {Channel}.", Channel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await connection.DisposeAsync();
            _logger.LogWarning(ex, "Activity listener failed to connect; retrying shortly.");
            // Brief pause so a database that is down does not spin the dispatcher loop.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_connection is null)
            return;

        try { await _connection.DisposeAsync(); }
        catch { /* connection already broken; nothing to salvage */ }
        _connection = null;
    }

    public async ValueTask DisposeAsync() => await DisposeConnectionAsync();
}
