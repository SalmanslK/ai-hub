namespace App.Connections;

// The usage a simulated connector reads in place of a real AI tool's API (FSD §1: "additional
// integrations may be simulated").
public sealed record SimulatedUsage(string Tool, IReadOnlyList<SimulatedUsageRecord> Records)
{
    private const string SampleTool = "simulated-chat";
    private static readonly DateTimeOffset SampleAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    // The usage the host registers: a few prompts and completions by sample employees.
    public static SimulatedUsage Sample() => new(SampleTool,
    [
        new(SampleTool, "sample-employee-1", SampleAt, "prompt", """{"words":42,"tokens":96}"""),
        new(SampleTool, "sample-employee-1", SampleAt.AddSeconds(4), "completion", """{"words":180,"tokens":412}"""),
        new(SampleTool, "sample-employee-2", SampleAt.AddMinutes(17), "conversation", """{"turns":6,"tokens":1530}"""),
    ]);
}

// One simulated usage record, as a connector reads it from its tool.
public sealed record SimulatedUsageRecord(string Tool, string Employee, DateTimeOffset OccurredAt, string Kind, string Payload);
