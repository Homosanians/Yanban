using Microsoft.Extensions.Options;
using Yanban.Application.Common;
using Yanban.Infrastructure.Notifications;
using Yanban.Infrastructure.Storage;

namespace Yanban.Worker;

/// <summary>
/// The loop. The actual work lives in <see cref="OutboxProcessor"/> and
/// <see cref="StorageJanitor"/>, which is what the tests drive: a
/// <c>BackgroundService</c> is hard to assert against, and a claim loop is not.
///
/// <para>One process, three queues: emails to send, objects to delete, and abandoned uploads to
/// reap. They share a worker because they share a shape, a Postgres-backed queue drained by a
/// <c>SKIP LOCKED</c> claim, and none of them is busy enough to need its own.</para>
/// </summary>
public class OutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly EmailOptions _options;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(
        IServiceScopeFactory scopes,
        IOptions<EmailOptions> options,
        ILogger<OutboxWorker> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Outbox worker started; polling every {Interval}s, batches of {Batch}.",
            _options.PollIntervalSeconds, _options.BatchSize);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                // A fresh scope per pass: the DbContext is scoped, and a long-lived one would
                // accumulate every message it has ever tracked.
                using var scope = _scopes.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                var janitor = scope.ServiceProvider.GetRequiredService<StorageJanitor>();

                // Drain fully rather than one batch per poll, so a burst clears in a single pass.
                while (await processor.ProcessBatchAsync(stoppingToken) > 0) { }

                // Reap abandoned uploads first, so the objects they orphan are enqueued in time for
                // this same pass's drain to pick them up.
                await janitor.ReapAbandonedUploadsAsync(stoppingToken);
                while (await janitor.DrainDeletionsAsync(stoppingToken) > 0) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // An exception escaping ExecuteAsync stops a BackgroundService permanently, and a
                // notifier that quietly died is worse than one that is merely behind.
                _logger.LogError(ex, "Outbox poll failed; retrying on the next tick.");
            }
        }
    }
}
