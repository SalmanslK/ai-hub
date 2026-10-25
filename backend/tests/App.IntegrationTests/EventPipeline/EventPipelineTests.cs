using System.Text.Json;
using System.Text.Json.Nodes;
using App.Capture.Contracts;
using App.Processing.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace App.IntegrationTests.EventPipeline;

// The event pipeline seam (plan boilerplate-and-architecture §2, §3 Usage event, §4 flows 2–3):
// Capture stores first, its worker hands events to Processing at least once and in order,
// Processing is idempotent per event.
[Collection(PostgresCollection.Name)]
public sealed class EventPipelineTests(PostgresFixture postgres)
{
    private const string EventsTable = "capture.usage_events";
    private const string ProcessedTable = "processing.processed_events";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("AC", "R-006.AC-1")]
    public async Task Append_stores_one_row_with_the_values_and_runs_nothing_downstream()
    {
        var database = await postgres.CreateDatabaseAsync();
        using var host = new PipelineHost(postgres.ConnectionString(database), worker: false).Started();
        var usageEvent = NewEvent("chatgpt", "employee-17", "prompt");

        var id = await AppendAsync(host, usageEvent);

        Assert.NotEqual(Guid.Empty, id);
        var row = Assert.Single(await RowsAsync(database, EventsTable));
        AssertStores(row, id, usageEvent);
        Assert.Equal(0, await CountAsync(database, ProcessedTable, id));
    }

    [Theory]
    [Trait("AC", "R-006.AC-2")]
    [InlineData("Tool")]
    [InlineData("Employee")]
    [InlineData("Kind")]
    [InlineData("OccurredAt")]
    public async Task An_event_missing_a_required_field_is_refused_and_nothing_is_stored(string field)
    {
        var database = await postgres.CreateDatabaseAsync();
        using var host = new PipelineHost(postgres.ConnectionString(database), worker: false).Started();
        var complete = NewEvent("copilot", "employee-3", "completion");
        var usageEvent = field switch
        {
            "Tool" => complete with { Tool = null! },
            "Employee" => complete with { Employee = " " },
            "Kind" => complete with { Kind = "" },
            _ => complete with { OccurredAt = default },
        };

        var error = await Assert.ThrowsAnyAsync<ArgumentException>(() => AppendAsync(host, usageEvent));

        Assert.Contains(field, error.ParamName + " " + error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await RowsAsync(database, EventsTable));
    }

    [Fact]
    [Trait("AC", "R-006.AC-3")]
    public async Task A_stored_event_is_unchanged_after_it_is_handed_to_processing()
    {
        var database = await postgres.CreateDatabaseAsync();
        var connection = postgres.ConnectionString(database);
        Guid id;
        using (var capture = new PipelineHost(connection, worker: false).Started())
            id = await AppendAsync(capture, NewEvent("claude", "employee-8", "conversation"));
        var before = Assert.Single(await RowsAsync(database, EventsTable));

        using (new PipelineHost(connection).Started())
            await WaitUntilAsync(async () => await CountAsync(database, ProcessedTable, id) == 1,
                "the worker hands the event to Processing");

        var after = Assert.Single(await RowsAsync(database, EventsTable));
        Assert.Equal(before, after);
    }

    [Fact]
    [Trait("AC", "R-006.AC-4")]
    public async Task The_worker_hands_every_event_to_processing_in_store_order_once_each()
    {
        var database = await postgres.CreateDatabaseAsync();
        var calls = new Calls();
        using var host = new PipelineHost(postgres.ConnectionString(database),
            processor: (_, inner) => new RecordingProcessor(inner, calls)).Started();

        var ids = new List<Guid>();
        foreach (var kind in new[] { "first", "second", "third" })
            ids.Add(await AppendAsync(host, NewEvent("gemini", "employee-5", kind)));
        await WaitUntilAsync(() => Task.FromResult(ids.All(calls.Handled.Contains)), "all three events are handled");

        Assert.Equal(ids, calls.Handled.Distinct().ToList());
        foreach (var id in ids)
            Assert.Equal(1, await CountAsync(database, ProcessedTable, id));
    }

    [Fact]
    [Trait("AC", "R-006.AC-5")]
    public async Task A_failure_on_one_event_is_retried_without_losing_or_skipping_and_logged_without_payload()
    {
        var database = await postgres.CreateDatabaseAsync();
        var connection = postgres.ConnectionString(database);
        var secret = "secret-prompt-" + Guid.NewGuid().ToString("N");
        var ids = new List<Guid>();
        using (var capture = new PipelineHost(connection, worker: false).Started())
        {
            foreach (var kind in new[] { "first", "second", "third" })
                ids.Add(await AppendAsync(capture, NewEvent("chatgpt", "employee-9", kind, secret)));
        }
        const int failures = 2;
        var calls = new Calls();

        using var host = new PipelineHost(connection,
            processor: (_, inner) => new RecordingProcessor(inner, calls, failing: ids[1], failFirst: failures)).Started();
        await WaitUntilAsync(() => Task.FromResult(ids.All(calls.Handled.Contains)),
            "all three events are handled once the failure clears");

        Assert.Equal(failures, calls.Failures(ids[1]));
        Assert.Equal(ids, calls.Handled.Distinct().ToList());
        var thirdFirstSeen = calls.All.ToList().FindIndex(c => c.Id == ids[2]);
        var secondHandled = calls.All.ToList().FindIndex(c => c.Id == ids[1] && !c.Failed);
        Assert.True(secondHandled < thirdFirstSeen, "The third event reached Processing before the second was handled.");
        foreach (var id in ids)
            Assert.Equal(1, await CountAsync(database, ProcessedTable, id));

        var logged = host.Logs.Entries
            .Where(e => e.Level >= LogLevel.Warning && e.Text.Contains(ids[1].ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(failures, logged.Count);
        var leaks = host.Logs.Entries.Where(e => e.Text.Contains(secret, StringComparison.Ordinal)).ToList();
        Assert.True(leaks.Count == 0, "A log entry carries the payload:\n" + string.Join("\n---\n", leaks.Select(e => e.Text)));
    }

    [Fact]
    [Trait("AC", "R-006.AC-6")]
    public async Task A_redelivered_event_is_processed_once()
    {
        var database = await postgres.CreateDatabaseAsync();
        using var host = new PipelineHost(postgres.ConnectionString(database), worker: false).Started();
        var capturedEvent = new CapturedEvent(Guid.NewGuid(), "copilot", "employee-21",
            new DateTimeOffset(2026, 10, 1, 9, 30, 15, TimeSpan.Zero), "completion", """{"tokens":42}""");

        await HandOverAsync(host, capturedEvent);
        var once = await RowsAsync(database, ProcessedTable);
        await HandOverAsync(host, capturedEvent);
        var twice = await RowsAsync(database, ProcessedTable);

        Assert.Equal(1, await CountAsync(database, ProcessedTable, capturedEvent.Id));
        Assert.Equal(once, twice);
    }

    [Fact]
    [Trait("AC", "R-006.AC-7")]
    public async Task Events_not_handed_over_before_a_stop_are_handed_over_after_the_next_start()
    {
        var database = await postgres.CreateDatabaseAsync();
        var connection = postgres.ConnectionString(database);
        var ids = new List<Guid>();
        using (var before = new PipelineHost(connection, worker: false).Started())
        {
            foreach (var kind in new[] { "first", "second", "third" })
                ids.Add(await AppendAsync(before, NewEvent("claude", "employee-4", kind)));
        }
        foreach (var id in ids)
            Assert.Equal(0, await CountAsync(database, ProcessedTable, id));

        using (new PipelineHost(connection).Started())
        {
            await WaitUntilAsync(async () =>
            {
                foreach (var id in ids)
                    if (await CountAsync(database, ProcessedTable, id) != 1)
                        return false;
                return true;
            }, "every event stored before the stop is handed over after the start");
        }
    }

    [Fact]
    [Trait("AC", "R-006.AC-8")]
    public async Task Start_up_creates_the_pipeline_tables_in_their_own_schemas_once()
    {
        var database = await postgres.CreateDatabaseAsync();
        var connection = postgres.ConnectionString(database);

        using (new PipelineHost(connection).Started())
        {
        }
        var tables = await ModuleSchemas.TablesAsync(postgres, database);
        Assert.Contains(EventsTable, tables);
        Assert.Contains(ProcessedTable, tables);
        var histories = new Dictionary<string, IReadOnlyList<string>?>();
        foreach (var module in new[] { "Capture", "Processing" })
        {
            var schema = ModuleSchemas.Schema(module);
            var own = ModuleSchemas.Migrations(module);
            Assert.True(own.Count > 1, $"App.{module} carries no migration beyond its schema's.");
            histories[schema] = await ModuleSchemas.HistoryAsync(postgres, database, schema);
            Assert.Equal(own, histories[schema]);
        }

        using (new PipelineHost(connection).Started())
        {
        }
        Assert.Equal(tables, await ModuleSchemas.TablesAsync(postgres, database));
        foreach (var (schema, history) in histories)
            Assert.Equal(history, await ModuleSchemas.HistoryAsync(postgres, database, schema));
    }

    private static UsageEvent NewEvent(string tool, string employee, string kind, string? secret = null) =>
        new(tool, employee, new DateTimeOffset(2026, 10, 1, 9, 30, 15, TimeSpan.Zero), kind,
            JsonSerializer.Serialize(new { prompt = secret ?? "summarise the quarterly report", tokens = 128 }));

    private static Task<Guid> AppendAsync(PipelineHost host, UsageEvent usageEvent) =>
        host.InScopeAsync(services => services.GetRequiredService<IUsageEventLog>()
            .AppendAsync(usageEvent, CancellationToken.None));

    private static Task<bool> HandOverAsync(PipelineHost host, CapturedEvent capturedEvent) =>
        host.InScopeAsync(async services =>
        {
            await services.GetRequiredService<IUsageEventProcessor>().ProcessAsync(capturedEvent, CancellationToken.None);
            return true;
        });

    // Each row of a table as its JSON, ordered so two reads compare.
    private async Task<IReadOnlyList<string>> RowsAsync(string database, string table) =>
        (await postgres.QueryAsync(database, $"SELECT row_to_json(t)::text FROM {table} t ORDER BY 1;"))
        .Select(row => row[0])
        .ToList();

    // The rows of a table that carry an event id in any column.
    private async Task<int> CountAsync(string database, string table, Guid id) =>
        (await RowsAsync(database, table)).Count(row => row.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));

    // The stored row carries the id, each field's value and the payload, whatever its columns are named.
    private static void AssertStores(string row, Guid id, UsageEvent usageEvent)
    {
        var values = JsonNode.Parse(row)!.AsObject().Select(p => p.Value).ToList();
        var texts = values.Where(v => v is JsonValue).Select(v => v!.ToString()).ToList();
        Assert.Contains(id.ToString(), texts, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(usageEvent.Tool, texts);
        Assert.Contains(usageEvent.Employee, texts);
        Assert.Contains(usageEvent.Kind, texts);
        Assert.Contains(texts, t => DateTimeOffset.TryParse(t, out var time) && time == usageEvent.OccurredAt);
        var payload = JsonNode.Parse(usageEvent.Payload);
        Assert.Contains(values, v => JsonNode.DeepEquals(AsJson(v), payload));
    }

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
