using System.Net;
using System.Text.Json;
using Xunit;

namespace App.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("AC", "R-005.AC-1")]
    public async Task Start_up_creates_a_schema_per_module_with_its_own_applied_migrations()
    {
        var database = await postgres.CreateDatabaseAsync();

        using (var host = new IntegrationHost(postgres.ConnectionString(database)))
            host.CreateClient();

        var schemas = await ModuleSchemas.SchemasAsync(postgres, database);
        foreach (var module in ModuleSchemas.Modules)
        {
            var schema = ModuleSchemas.Schema(module);
            Assert.True(schemas.Contains(schema), $"No `{schema}` schema after start-up. Schemas: {string.Join(", ", schemas)}");
            var history = await ModuleSchemas.HistoryAsync(postgres, database, schema);
            Assert.True(history is not null, $"`{schema}` has no {ModuleSchemas.HistoryTable} table.");
            var migrations = ModuleSchemas.Migrations(module);
            Assert.True(migrations.Count > 0, $"App.{module} carries no migration.");
            Assert.Equal(migrations, history);
        }
    }

    [Fact]
    [Trait("AC", "R-005.AC-2")]
    public async Task A_second_start_applies_nothing_twice_and_changes_nothing()
    {
        var database = await postgres.CreateDatabaseAsync();
        var connection = postgres.ConnectionString(database);

        using (var first = new IntegrationHost(connection))
            first.CreateClient();
        var before = await SnapshotAsync(database);

        using (var second = new IntegrationHost(connection))
        {
            var response = await second.CreateClient().GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var after = await SnapshotAsync(database);

        Assert.True(before.Histories.Count == ModuleSchemas.Modules.Length && before.Histories.All(h => h.Value.Count > 0),
            "The first start did not apply each module's migrations: " + string.Join(", ", before.Histories.Keys));
        Assert.Equal(before.Tables, after.Tables);
        foreach (var (schema, history) in before.Histories)
        {
            Assert.Equal(history, after.Histories[schema]);
            Assert.Equal(history.Count, history.Distinct().Count());
        }
    }

    [Fact]
    [Trait("AC", "R-005.AC-3")]
    public async Task Every_table_is_in_its_own_module_schema_and_no_history_lists_another_module()
    {
        var database = await postgres.CreateDatabaseAsync();

        using (var host = new IntegrationHost(postgres.ConnectionString(database)))
            host.CreateClient();

        var moduleSchemas = ModuleSchemas.Modules.Select(ModuleSchemas.Schema).ToHashSet();
        var tables = await ModuleSchemas.TablesAsync(postgres, database);
        var outside = tables.Where(t => !moduleSchemas.Contains(t.Split('.')[0])).ToList();
        Assert.True(outside.Count == 0, "Tables outside the module schemas: " + string.Join(", ", outside));
        foreach (var module in ModuleSchemas.Modules)
        {
            var schema = ModuleSchemas.Schema(module);
            Assert.Contains($"{schema}.{ModuleSchemas.HistoryTable}", tables);
            var history = await ModuleSchemas.HistoryAsync(postgres, database, schema) ?? [];
            var own = ModuleSchemas.Migrations(module);
            Assert.True(own.Count > 0, $"App.{module} carries no migration.");
            var foreign = history.Except(own).ToList();
            Assert.True(foreign.Count == 0, $"`{schema}` history lists migrations App.{module} does not carry: " +
                                            string.Join(", ", foreign));
            Assert.Equal(own, history);
        }
    }

    [Fact]
    [Trait("AC", "R-005.AC-4")]
    public async Task Health_reports_process_and_database_healthy_without_authentication()
    {
        var database = await postgres.CreateDatabaseAsync();
        using var host = new IntegrationHost(postgres.ConnectionString(database));

        var response = await host.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var checks = Checks(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", checks.GetValueOrDefault("process"));
        Assert.Equal("Healthy", checks.GetValueOrDefault("database"));
    }

    [Fact]
    [Trait("AC", "R-005.AC-5")]
    public async Task Health_answers_503_when_the_database_is_unreachable_and_reveals_no_credentials()
    {
        // A container of its own, so stopping it leaves the others' database alone.
        await using var container = await ContainerRuntime.StartPostgresAsync(PostgresFixture.NewPostgres());
        var connection = PostgresFixture.ConnectionString(container, "aihub_it");
        using var host = new IntegrationHost(connection);
        var client = host.CreateClient();

        await container.StopAsync();
        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var checks = Checks(body);
        Assert.Equal("Unhealthy", checks.GetValueOrDefault("database"));
        Assert.Equal("Healthy", checks.GetValueOrDefault("process"));
        foreach (var secret in new[] { connection, PostgresFixture.Password, PostgresFixture.User })
        {
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
            var leaks = host.Logs.Entries.Where(e => e.Contains(secret, StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(leaks.Count == 0, "A log entry carries a credential:\n" + string.Join("\n---\n", leaks));
        }
    }

    private async Task<(IReadOnlyList<string> Tables, Dictionary<string, IReadOnlyList<string>> Histories)> SnapshotAsync(
        string database)
    {
        var histories = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var module in ModuleSchemas.Modules)
        {
            var schema = ModuleSchemas.Schema(module);
            if (await ModuleSchemas.HistoryAsync(postgres, database, schema) is { } history)
                histories[schema] = history;
        }
        return (await ModuleSchemas.TablesAsync(postgres, database), histories);
    }

    // The health body's checks, by name.
    private static Dictionary<string, string?> Checks(string body)
    {
        using var document = JsonDocument.Parse(body);
        var checks = document.RootElement.EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, "checks", StringComparison.OrdinalIgnoreCase));
        Assert.True(checks.Value.ValueKind == JsonValueKind.Array, "The health body has no `checks` array: " + body);
        return checks.Value.EnumerateArray().ToDictionary(
            check => Member(check, "name") ?? "",
            check => Member(check, "status"));
    }

    private static string? Member(JsonElement element, string name) =>
        element.EnumerateObject()
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            .Value is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
