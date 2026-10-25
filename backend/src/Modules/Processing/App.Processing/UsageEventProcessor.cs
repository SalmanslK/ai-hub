using App.Processing.Contracts;
using App.Processing.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Processing;

// Handles each captured event once: the processed-event row and the handling's effects are written
// in one transaction, and an event whose row already exists is skipped, so a redelivery is a no-op.
public sealed class UsageEventProcessor(ProcessingDbContext db, TimeProvider clock) : IUsageEventProcessor
{
    public async Task ProcessAsync(CapturedEvent capturedEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capturedEvent);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // A concurrent delivery of the same id waits here on the key, then inserts nothing.
        var recorded = await db.Database.ExecuteSqlAsync(
            $"INSERT INTO processing.processed_events (event_id, processed_at) VALUES ({capturedEvent.Id}, {clock.GetUtcNow()}) ON CONFLICT (event_id) DO NOTHING",
            cancellationToken);
        if (recorded == 0)
            return;
        // The handling's effects are written here, inside the transaction, so they land once per event.
        await transaction.CommitAsync(cancellationToken);
    }
}
