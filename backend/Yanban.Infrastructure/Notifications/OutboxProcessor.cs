using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yanban.Application.Abstractions;
using Yanban.Application.Common;
using Yanban.Domain.Enums;
using Yanban.Infrastructure.Persistence;

namespace Yanban.Infrastructure.Notifications;

/// <summary>
/// Drains the notification outbox. One pass claims a batch, sends it, and records the outcome.
///
/// <para>Why there is no cursor here. The realtime tailer needs a grace window because its
/// high-water mark orders by <c>sequence</c>, an identity value assigned at INSERT but visible
/// only at COMMIT, so a row can commit behind the cursor and be skipped forever. This claims by
/// <c>status</c> instead. There is no high-water mark to fall behind: every pass re-asks which
/// rows are still Pending, so a late-committing row is picked up the moment it becomes visible.</para>
///
/// <para>SKIP LOCKED is what makes this safe to run more than once. The claim locks the rows it
/// takes and steps over rows another worker already holds, so N workers partition the queue
/// instead of racing for it. (The SignalR tailer is the opposite: every instance runs one, because
/// duplicate pushes are harmless. Duplicate emails are not.)</para>
///
/// <para>Delivery is at-least-once. The row lock is held across the SMTP send, so a crash after
/// the relay accepts but before the transaction commits leaves the row Pending and it will be
/// sent again. Marking Sent first would instead turn a crash into a mail that is never sent at
/// all; a duplicate email is preferable to a dropped one.</para>
/// </summary>
public class OutboxProcessor
{
    private readonly YanbanDbContext _db;
    private readonly IEmailSender _sender;
    private readonly EmailRenderer _renderer;
    private readonly EmailOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        YanbanDbContext db,
        IEmailSender sender,
        IOptions<EmailOptions> options,
        ILogger<OutboxProcessor> logger)
    {
        _db = db;
        _sender = sender;
        _options = options.Value;
        _renderer = new EmailRenderer(_options);
        _logger = logger;
    }

    /// <summary>Runs one pass. Returns how many messages were claimed (not how many were sent).</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // `outbox_messages` has no concurrency token, so SELECT * is safe for FromSql, and
        // ToListAsync runs the statement verbatim, so FOR UPDATE SKIP LOCKED stays at the top
        // level rather than being wrapped into a subquery, where it would not apply.
        var batch = await _db.OutboxMessages
            .FromSql($"""
                      SELECT * FROM outbox_messages
                      WHERE status = 'Pending' AND next_attempt_at <= now()
                      ORDER BY created_at
                      LIMIT {_options.BatchSize}
                      FOR UPDATE SKIP LOCKED
                      """)
            .ToListAsync(ct);

        if (batch.Count == 0)
        {
            await tx.CommitAsync(ct);
            return 0;
        }

        foreach (var message in batch)
        {
            try
            {
                await _sender.SendAsync(_renderer.Render(message), ct);

                message.Status = OutboxStatus.Sent;
                message.SentAt = DateTimeOffset.UtcNow;
                message.Attempts += 1;
                // A confirmation payload carries a working token. A spent message has no business
                // keeping a live credential at rest, and the row is still evidence without it.
                message.Payload = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts += 1;
                message.LastError = Truncate(ex.Message, 1000);

                if (message.Attempts >= _options.MaxAttempts)
                {
                    // Dead letter. Kept, not deleted: a message we failed to send is exactly the
                    // thing someone will want to look at.
                    message.Status = OutboxStatus.Failed;
                    _logger.LogError(ex, "Outbox message {Id} failed permanently after {Attempts} attempts.",
                        message.Id, message.Attempts);
                }
                else
                {
                    // Exponential, capped: 2s, 4s, 8s, 16s.
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, message.Attempts));
                    message.NextAttemptAt = DateTimeOffset.UtcNow.Add(delay);
                    _logger.LogWarning(ex, "Outbox message {Id} failed (attempt {Attempts}); retrying in {Delay}.",
                        message.Id, message.Attempts, delay);
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return batch.Count;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
