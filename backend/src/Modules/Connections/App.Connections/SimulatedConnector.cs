using App.Capture.Contracts;
using App.Connections.Contracts;
using Microsoft.Extensions.Logging;

namespace App.Connections;

// A connector for a simulated AI tool: appends one usage event per simulated record through Capture's
// contract, and never writes any module's data itself.
public sealed partial class SimulatedConnector(SimulatedUsage usage, IUsageEventLog events, ILogger<SimulatedConnector> logger)
    : IConnector
{
    public string Tool => usage.Tool;

    public async Task<ConnectorRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var appended = 0;
        var rejected = new List<RejectedRecord>();
        for (var index = 0; index < usage.Records.Count; index++)
        {
            var record = usage.Records[index];
            try
            {
                await events.AppendAsync(
                    new UsageEvent(record.Tool, record.Employee, record.OccurredAt, record.Kind, record.Payload),
                    cancellationToken);
                appended++;
            }
            catch (ArgumentException refused)
            {
                // Capture names the field it refused; the record's values stay out of the result and the log.
                rejected.Add(new RejectedRecord(index, refused.ParamName ?? "unknown"));
            }
        }

        LogRun(Tool, appended, rejected.Count);
        return new ConnectorRunResult(appended, rejected);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Connector run for {Tool}: {Appended} events appended, {Rejected} records rejected")]
    private partial void LogRun(string tool, int appended, int rejected);
}
