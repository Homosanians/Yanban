using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Activities;
using Yanban.Application.Common;
using Yanban.Application.Realtime;
using Yanban.Domain.Entities;
using Yanban.Domain.Enums;

namespace Yanban.API.Realtime;

/// <summary>
/// Tails the activity log and pushes each new event to the clients watching that board.
///
/// <para>Every mutation writes an activity row inside its own transaction, which makes that
/// table an outbox: durable, ordered, and committed atomically with the change it describes.
/// This is its reader. Nothing publishes from the request path, so an event can never be
/// announced for a change that then rolled back.</para>
///
/// <para>There is no Redis backplane. Without one, <c>Clients.Group(...)</c> reaches only
/// the connections held by this instance, which is what is wanted: every instance runs this
/// tailer over the same shared log, and each client is connected to exactly one instance, so
/// every client is served by the instance it is on. The durable outbox is the backplane.</para>
/// </summary>
public class ActivityDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHubContext<BoardHub, IBoardClient> _hub;
    private readonly BoardSubscriptionRegistry _registry;
    private readonly RealtimeOptions _options;
    private readonly ILogger<ActivityDispatcher> _logger;

    private OutboxCursor? _cursor;

    public ActivityDispatcher(
        IServiceScopeFactory scopes,
        IHubContext<BoardHub, IBoardClient> hub,
        BoardSubscriptionRegistry registry,
        IOptions<RealtimeOptions> options,
        ILogger<ActivityDispatcher> logger)
    {
        _scopes = scopes;
        _hub = hub;
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.PollIntervalMs));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // An exception escaping ExecuteAsync stops a BackgroundService for good and
                // says nothing: one transient database blip would silently take realtime down
                // for the life of the process. Log it and let the next tick retry.
                _logger.LogError(ex, "Outbox poll failed; retrying on the next tick.");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IActivityOutbox>();

        // Start at the head: history is not replayed to clients that were not there for it.
        // Initialized lazily so a database that is not up yet costs a retried tick rather
        // than the whole service.
        _cursor ??= new OutboxCursor(
            await outbox.GetLatestSequenceAsync(ct),
            TimeSpan.FromSeconds(_options.GraceSeconds));

        var visible = await outbox.ReadSinceAsync(_cursor.SafeSequence, _options.BatchSize, ct);
        if (visible.Count == 0)
            return;

        foreach (var activity in _cursor.Advance(visible, DateTimeOffset.UtcNow))
        {
            await _hub.Clients.Group(BoardHub.GroupFor(activity.BoardId)).ActivityOccurred(activity);

            if (IsMemberRemoval(activity))
                await EvictAsync(activity.BoardId, activity.EntityId, ct);
        }
    }

    // The removed member's own id is the entity this event is about (BoardService).
    private static bool IsMemberRemoval(ActivityDto activity) =>
        activity.EntityType == ActivityEntityTypes.Member &&
        activity.Action == nameof(ActivityAction.Deleted);

    /// <summary>
    /// Cuts a removed member's live connections off this board. Sent after the removal
    /// event itself, so they learn why the feed stopped. Eventual, not synchronous: it
    /// happens a poll interval after the removal commits, a bounded and deliberate lag, not
    /// an open-ended one.
    /// </summary>
    private async Task EvictAsync(Guid boardId, Guid userId, CancellationToken ct)
    {
        foreach (var connectionId in _registry.ConnectionsFor(boardId, userId))
        {
            await _hub.Groups.RemoveFromGroupAsync(connectionId, BoardHub.GroupFor(boardId), ct);
            await _hub.Clients.Client(connectionId).BoardAccessRevoked(boardId);
        }

        _registry.RemoveAll(boardId, userId);
    }
}
