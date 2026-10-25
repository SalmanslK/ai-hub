using System.Text.Json;
using App.Capture.Contracts;
using App.IntegrationTests.EventPipeline;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace App.IntegrationTests.Connections;

// Guard for the connector seam: a caller other than a connector still appends through Capture's contract
// and the R-006 pipeline holds — stored first, handed to Processing in order, processed once per id.
[Collection(PostgresCollection.Name)]
public sealed class PipelineGuardTests(PostgresFixture postgres)
{
    private const string EventsTable = "capture.usage_events";
    private const string ProcessedTable = "processing.processed_events";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    [Trait("AC", "R-007.AC-9")]
    public async Task Events_appended_directly_are_stored_then_processed_in_order_once_each()
    {
        var database = await postgres.CreateDatabaseAsync();
        var calls = new Calls();
        using var host = new PipelineHost(postgres.ConnectionString(database),
            processor: (_, inner) => new RecordingProcessor(inner, calls)).Started();

        var ids = new List<Guid>();
        foreach (var kind in new[] { "first", "second", "third" })
            ids.Add(await host.InScopeAsync(services => services.GetRequiredService<IUsageEventLog>()
                .AppendAsync(new UsageEvent("chatgpt", "employee-61", new DateTimeOffset(2026, 10, 1, 9, 30, 15, TimeSpan.Zero),
                    kind, JsonSerializer.Serialize(new { tokens = 12 })), CancellationToken.None)));
        var stored = await RowsAsync(database, EventsTable);
        Assert.All(ids, id => Assert.Single(stored, row => row.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)));

        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline && !ids.All(calls.Handled.Contains))
            await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.Equal(ids, calls.Handled.Distinct().ToList());
        var processed = await RowsAsync(database, ProcessedTable);
        foreach (var id in ids)
            Assert.Single(processed, row => row.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<string>> RowsAsync(string database, string table) =>
        (await postgres.QueryAsync(database, $"SELECT row_to_json(t)::text FROM {table} t ORDER BY 1;"))
        .Select(row => row[0])
        .ToList();
}
