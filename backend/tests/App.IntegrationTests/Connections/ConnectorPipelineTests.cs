using System.Text.Json;
using System.Text.Json.Nodes;
using App.Connections;
using Xunit;

namespace App.IntegrationTests.Connections;

// A connector end to end (FSD §2: Connection → Event Capture → Data Processing): the simulated connector
// the host registers appends through Capture's contract, Capture stores, its worker hands to Processing.
[Collection(PostgresCollection.Name)]
public sealed class ConnectorPipelineTests(PostgresFixture postgres)
{
    private const string Tool = "simulated-chat";
    private const string EventsTable = "capture.usage_events";
    private const string ProcessedTable = "processing.processed_events";
    private static readonly DateTimeOffset At = new(2026, 10, 1, 9, 30, 15, TimeSpan.Zero);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("AC", "R-007.AC-3")]
    public async Task A_simulated_run_stores_one_row_per_record_and_processing_records_each()
    {
        var database = await postgres.CreateDatabaseAsync();
        var records = new[]
        {
            Record("employee-31", "prompt", At),
            Record("employee-32", "completion", At.AddMinutes(4)),
            Record("employee-33", "conversation", At.AddMinutes(11)),
        };
        using var host = new ConnectorHost(postgres.ConnectionString(database), new SimulatedUsage(Tool, records)).Started();

        var result = await host.RunSimulatedConnectorAsync();

        Assert.Equal(records.Length, result.Appended);
        Assert.Empty(result.Rejected);
        var rows = await RowsAsync(database, EventsTable);
        Assert.Equal(records.Length, rows.Count);
        foreach (var record in records)
            Assert.Single(rows, row => Stores(row, record));
        var ids = rows.Select(IdOf).ToList();
        await WaitUntilAsync(async () =>
        {
            foreach (var id in ids)
                if (await CountAsync(database, ProcessedTable, id) != 1)
                    return false;
            return true;
        }, "Processing records one processed-event row per appended event");
    }

    [Theory]
    [Trait("AC", "R-007.AC-4")]
    [InlineData("Tool")]
    [InlineData("Payload")]
    public async Task A_record_capture_refuses_is_reported_and_not_stored(string field)
    {
        var database = await postgres.CreateDatabaseAsync();
        var good = Record("employee-41", "prompt", At);
        var bad = field == "Tool" ? Record("employee-42", "prompt", At) with { Tool = "" }
            : Record("employee-42", "prompt", At) with { Payload = "not json" };
        var last = Record("employee-43", "prompt", At);
        using var host = new ConnectorHost(postgres.ConnectionString(database),
            new SimulatedUsage(Tool, [good, bad, last]), worker: false).Started();

        var result = await host.RunSimulatedConnectorAsync();

        Assert.Equal(2, result.Appended);
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(1, rejected.Index);
        Assert.Equal(field, rejected.Field);
        var rows = await RowsAsync(database, EventsTable);
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, row => row.Contains(bad.Employee, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AC", "R-007.AC-5")]
    public async Task A_run_writes_only_capture_usage_events()
    {
        var database = await postgres.CreateDatabaseAsync();
        var records = new[] { Record("employee-51", "prompt", At), Record("employee-52", "prompt", At) };
        using var host = new ConnectorHost(postgres.ConnectionString(database),
            new SimulatedUsage(Tool, records), worker: false).Started();
        var tables = await ModuleSchemas.TablesAsync(postgres, database);
        var before = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var table in tables)
            before[table] = await RowsAsync(database, table);

        await host.RunSimulatedConnectorAsync();

        var changed = new List<string>();
        foreach (var table in tables)
            if (!before[table].SequenceEqual(await RowsAsync(database, table)))
                changed.Add(table);
        Assert.Equal([EventsTable], changed);
        Assert.Equal(records.Length, (await RowsAsync(database, EventsTable)).Count);
    }

    private static SimulatedUsageRecord Record(string employee, string kind, DateTimeOffset at) =>
        new(Tool, employee, at, kind, JsonSerializer.Serialize(new { prompt = "draft the release notes", tokens = 64 }));

    // Each row of a table as its JSON, ordered so two reads compare.
    private async Task<IReadOnlyList<string>> RowsAsync(string database, string table)
    {
        var quoted = string.Join(".", table.Split('.').Select(part => $"\"{part}\""));
        return (await postgres.QueryAsync(database, $"SELECT row_to_json(t)::text FROM {quoted} t ORDER BY 1;"))
            .Select(row => row[0])
            .ToList();
    }

    private async Task<int> CountAsync(string database, string table, Guid id) =>
        (await RowsAsync(database, table)).Count(row => row.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));

    // The stored row carries each of the record's values, whatever its columns are named.
    private static bool Stores(string row, SimulatedUsageRecord record)
    {
        var values = JsonNode.Parse(row)!.AsObject().Select(p => p.Value).ToList();
        var texts = values.Where(v => v is JsonValue).Select(v => v!.ToString()).ToList();
        var payload = JsonNode.Parse(record.Payload);
        return texts.Contains(record.Tool)
               && texts.Contains(record.Employee)
               && texts.Contains(record.Kind)
               && texts.Any(t => DateTimeOffset.TryParse(t, out var time) && time == record.OccurredAt)
               && values.Any(v => JsonNode.DeepEquals(AsJson(v), payload));
    }

    private static Guid IdOf(string row) =>
        JsonNode.Parse(row)!.AsObject()
            .Select(p => p.Value)
            .Where(v => v is JsonValue)
            .Select(v => Guid.TryParse(v!.ToString(), out var id) ? id : Guid.Empty)
            .First(id => id != Guid.Empty);

    private static JsonNode? AsJson(JsonNode? value)
    {
        if (value is JsonValue text && text.TryGetValue<string>(out var s))
        {
            try
            {
                return JsonNode.Parse(s);
            }
            catch (JsonException)
            {
                return value;
            }
        }
        return value;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        Assert.Fail($"Timed out after {Patience.TotalSeconds}s waiting until {what}.");
    }
}
