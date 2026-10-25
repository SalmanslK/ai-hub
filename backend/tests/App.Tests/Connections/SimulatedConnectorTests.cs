using System.Text.Json;
using App.Capture.Contracts;
using App.Connections;
using App.Connections.Contracts;
using App.Tests.Host;
using Microsoft.Extensions.Logging;
using Xunit;

namespace App.Tests.Connections;

// The simulated connector (FSD §1: "additional integrations may be simulated"): it reads its simulated
// usage and appends one event per record through Capture's contract only, reporting what it appended
// and what Capture refused, and logs counts — never an employee or a payload (FSD §7).
public sealed class SimulatedConnectorTests
{
    private const string Tool = "simulated-chat";
    private static readonly DateTimeOffset At = new(2026, 10, 1, 9, 30, 15, TimeSpan.Zero);

    [Fact]
    [Trait("AC", "R-007.AC-2")]
    public async Task A_run_appends_one_event_per_record_with_its_values()
    {
        var records = new[]
        {
            Record("employee-1", "prompt", At),
            Record("employee-2", "completion", At.AddMinutes(5)),
            Record("employee-3", "conversation", At.AddMinutes(9)),
        };
        var log = new FakeUsageEventLog();
        IConnector connector = Connector(records, log, out _);

        var result = await connector.RunAsync(CancellationToken.None);

        Assert.Equal(Tool, connector.Tool);
        Assert.Equal(records.Length, result.Appended);
        Assert.Empty(result.Rejected);
        Assert.Equal(
            records.Select(r => new UsageEvent(r.Tool, r.Employee, r.OccurredAt, r.Kind, r.Payload)),
            log.Appended);
    }

    [Theory]
    [Trait("AC", "R-007.AC-4")]
    [InlineData("Tool")]
    [InlineData("Employee")]
    [InlineData("Kind")]
    [InlineData("OccurredAt")]
    [InlineData("Payload")]
    public async Task A_record_capture_refuses_is_reported_and_the_rest_are_appended(string field)
    {
        var good = Record("employee-1", "prompt", At);
        var bad = Broken(Record("employee-2", "completion", At), field);
        var last = Record("employee-3", "conversation", At);
        var log = new FakeUsageEventLog();
        var connector = Connector([good, bad, last], log, out _);

        var result = await connector.RunAsync(CancellationToken.None);

        Assert.Equal(2, result.Appended);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(1, rejected.Index);
        Assert.Equal(field, rejected.Field);
        Assert.Equal(new[] { good.Employee, last.Employee }, log.Appended.Select(e => e.Employee));
    }

    [Fact]
    [Trait("AC", "R-007.AC-6")]
    public async Task A_run_logs_the_tool_and_counts_and_no_employee_or_payload()
    {
        var secret = "secret-prompt-" + Guid.NewGuid().ToString("N");
        var records = new[]
        {
            Record("employee-a7", "prompt", At, secret),
            Record("employee-b8", "prompt", At, secret),
            Record("employee-c9", "prompt", At, secret),
            Broken(Record("employee-d10", "prompt", At, secret), "Kind"),
            Broken(Record("employee-e11", "prompt", At, secret), "Payload") with { Payload = "not json " + secret },
        };
        var connector = Connector(records, new FakeUsageEventLog(), out var capture);

        await connector.RunAsync(CancellationToken.None);

        Assert.Contains(capture.Entries, e =>
            e.Message.Contains(Tool, StringComparison.Ordinal)
            && e.Message.Contains('3')
            && e.Message.Contains('2'));
        var employees = records.Select(r => r.Employee).ToList();
        var leaks = capture.Entries
            .Where(e => e.Text.Contains(secret, StringComparison.Ordinal)
                        || employees.Any(id => e.Text.Contains(id, StringComparison.Ordinal)))
            .ToList();
        Assert.True(leaks.Count == 0, "A log entry carries an employee or a payload:\n" +
                                      string.Join("\n---\n", leaks.Select(e => e.Text)));
    }

    private static SimulatedConnector Connector(IReadOnlyList<SimulatedUsageRecord> records, IUsageEventLog log,
        out LogCapture capture)
    {
        var sink = new LogCapture();
        capture = sink;
        var factory = LoggerFactory.Create(logging => logging.AddProvider(sink).SetMinimumLevel(LogLevel.Trace));
        return new SimulatedConnector(new SimulatedUsage(Tool, records), log, factory.CreateLogger<SimulatedConnector>());
    }

    private static SimulatedUsageRecord Record(string employee, string kind, DateTimeOffset at, string? prompt = null) =>
        new(Tool, employee, at, kind,
            JsonSerializer.Serialize(new { prompt = prompt ?? "summarise the quarterly report", tokens = 128 }));

    private static SimulatedUsageRecord Broken(SimulatedUsageRecord record, string field) => field switch
    {
        "Tool" => record with { Tool = "" },
        "Employee" => record with { Employee = " " },
        "Kind" => record with { Kind = "" },
        "OccurredAt" => record with { OccurredAt = default },
        _ => record with { Payload = "not json" },
    };
}

// Capture's contract as a connector sees it: refuses what Capture refuses, naming the field, and keeps
// what it accepts in order.
public sealed class FakeUsageEventLog : IUsageEventLog
{
    private readonly List<UsageEvent> _appended = [];

    public IReadOnlyList<UsageEvent> Appended => _appended;

    public Task<Guid> AppendAsync(UsageEvent usageEvent, CancellationToken cancellationToken)
    {
        Require(!string.IsNullOrWhiteSpace(usageEvent.Tool), nameof(UsageEvent.Tool));
        Require(!string.IsNullOrWhiteSpace(usageEvent.Employee), nameof(UsageEvent.Employee));
        Require(!string.IsNullOrWhiteSpace(usageEvent.Kind), nameof(UsageEvent.Kind));
        Require(usageEvent.OccurredAt != default, nameof(UsageEvent.OccurredAt));
        Require(IsJson(usageEvent.Payload), nameof(UsageEvent.Payload));
        _appended.Add(usageEvent);
        return Task.FromResult(Guid.CreateVersion7());
    }

    private static void Require(bool holds, string field)
    {
        if (!holds)
            throw new ArgumentException($"A usage event needs a valid {field}.", field);
    }

    private static bool IsJson(string? payload)
    {
        if (payload is null)
            return false;
        try
        {
            using (JsonDocument.Parse(payload))
                return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
