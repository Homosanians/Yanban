using Microsoft.Extensions.Options;
using Yanban.Application.Common;
using Yanban.Infrastructure.Notifications;

namespace Yanban.Worker;

/// <summary>
/// The loop. All of the interesting behaviour lives in <see cref="OutboxProcessor"/> — deliberately,
/// because that is what the tests drive: a <c>BackgroundService</c> is not something you can assert
/// against, and a claim loop very much is.
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

                // Drain rather than sip: a burst of 50 messages should not take five polls to clear.
                while (await processor.ProcessBatchAsync(stoppingToken) > 0) { }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // An exception escaping ExecuteAsync stops a BackgroundService permanently — and a
                // notifier that quietly died is worse than one that is merely behind.
                _logger.LogError(ex, "Outbox poll failed; retrying on the next tick.");
            }
        }
    }
}
