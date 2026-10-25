namespace App.Connections.Contracts;

// One AI tool's connector, real or simulated: it reads the tool's usage and hands each event to Capture.
public interface IConnector
{
    // The AI tool this connector serves.
    string Tool { get; }

    Task<ConnectorRunResult> RunAsync(CancellationToken cancellationToken);
}

// What one run did: the events Capture stored, and the records it refused.
public sealed record ConnectorRunResult(int Appended, IReadOnlyList<RejectedRecord> Rejected);

// A record Capture refused: its position in the run, and the field Capture named — never its values.
public sealed record RejectedRecord(int Index, string Field);
