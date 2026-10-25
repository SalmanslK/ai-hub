using System.Text.Json;
using App.Capture.Contracts;
using App.Capture.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Capture;

// Stores each event and returns; nothing downstream runs here — the worker hands it on later.
public sealed class UsageEventLog(CaptureDbContext db) : IUsageEventLog
{
    // Appends take this transaction-level lock, so store order (Position) is also commit order and the
    // worker, reading past its cursor, never passes an event that commits late.
    private const long AppendLock = 0x5553_4147_455F_4556; // "USAGE_EV"

    public async Task<Guid> AppendAsync(UsageEvent usageEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(usageEvent);
        RequireText(usageEvent.Tool, nameof(UsageEvent.Tool));
        RequireText(usageEvent.Employee, nameof(UsageEvent.Employee));
        RequireText(usageEvent.Kind, nameof(UsageEvent.Kind));
        if (usageEvent.OccurredAt == default)
            throw new ArgumentException($"A usage event needs its {nameof(UsageEvent.OccurredAt)}.", nameof(UsageEvent.OccurredAt));
        RequireJson(usageEvent.Payload);

        var stored = new StoredUsageEvent
        {
            Id = Guid.CreateVersion7(),
            Tool = usageEvent.Tool,
            Employee = usageEvent.Employee,
            OccurredAt = usageEvent.OccurredAt.ToUniversalTime(),
            Kind = usageEvent.Kind,
            Payload = usageEvent.Payload,
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AppendLock})", cancellationToken);
        db.UsageEvents.Add(stored);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return stored.Id;
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"A usage event needs its {field}.", field);
    }

    // The message names the field only: the payload's content never reaches an error or a log.
    private static void RequireJson(string? payload)
    {
        const string field = nameof(UsageEvent.Payload);
        if (payload is null)
            throw new ArgumentException($"A usage event needs its {field}.", field);
        try
        {
            using (JsonDocument.Parse(payload))
            {
            }
        }
        catch (JsonException)
        {
            throw new ArgumentException($"A usage event's {field} must be JSON.", field);
        }
    }
}
