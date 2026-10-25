using App.Capture.Data;
using App.Processing.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace App.Capture;

// Hands stored events to Processing in store order, at least once: an event's place is recorded only
// after Processing has taken it, so a failure or a restart hands it over again (Processing is
// idempotent per event). A failed event is retried before any later one.
public sealed partial class UsageEventWorker(IServiceScopeFactory scopes, ILogger<UsageEventWorker> logger)
    : BackgroundService
{
    private const string Consumer = "processing";
    private const int BatchSize = 100;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan? wait;
            try
            {
                wait = await HandOverBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogStoreUnavailable(exception);
                wait = RetryDelay;
            }
            if (wait is { } delay)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    // Hands over the next batch past the cursor. Returns how long to wait before the next pass:
    // none while a full batch went through, the idle delay when the store is drained, the retry
    // delay when Processing failed.
    private async Task<TimeSpan?> HandOverBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CaptureDbContext>();
        var processor = scope.ServiceProvider.GetRequiredService<IUsageEventProcessor>();

        var cursor = await db.HandOverCursors.SingleOrDefaultAsync(c => c.Consumer == Consumer, cancellationToken);
        if (cursor is null)
        {
            cursor = new HandOverCursor { Consumer = Consumer, Position = 0 };
            db.HandOverCursors.Add(cursor);
        }
        var batch = await db.UsageEvents.AsNoTracking()
            .Where(e => e.Position > cursor.Position)
            .OrderBy(e => e.Position)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var stored in batch)
        {
            try
            {
                await processor.ProcessAsync(new CapturedEvent(stored.Id, stored.Tool, stored.Employee,
                    stored.OccurredAt, stored.Kind, stored.Payload), cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogHandOverFailed(exception, stored.Id);
                return RetryDelay;
            }
            cursor.Position = stored.Position;
            await db.SaveChangesAsync(cancellationToken);
        }
        return batch.Count == BatchSize ? null : IdleDelay;
    }

    // The event id only: the payload never reaches a log.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Handing usage event {EventId} to Processing failed; it is retried before any later event")]
    private partial void LogHandOverFailed(Exception exception, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The usage-event worker could not read or advance its store; retrying")]
    private partial void LogStoreUnavailable(Exception exception);
}
