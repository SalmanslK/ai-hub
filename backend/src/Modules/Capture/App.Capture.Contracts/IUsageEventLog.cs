namespace App.Capture.Contracts;

// Capture's event store, append only: a stored event is never changed or removed. The call returns
// once the event is stored; nothing downstream runs inside it.
public interface IUsageEventLog
{
    // Stores the event and returns its id. Throws ArgumentException, naming the field, when the
    // tool, employee, kind or time is missing or the payload is not JSON.
    Task<Guid> AppendAsync(UsageEvent usageEvent, CancellationToken cancellationToken);
}
