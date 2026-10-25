using System.Net;
using Xunit;

namespace App.IntegrationTests.EventPipeline;

// With the pipeline's tables and worker in the host, health still reports the database as before.
[Collection(PostgresCollection.Name)]
public sealed class PipelineHealthTests(PostgresFixture postgres)
{
    [Fact]
    [Trait("AC", "R-006.AC-11")]
    public async Task Health_is_200_with_the_database_healthy_when_it_is_reachable()
    {
        var database = await postgres.CreateDatabaseAsync();
        using var host = new IntegrationHost(postgres.ConnectionString(database));

        var response = await host.CreateClient().GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(@"(?i)""name""\s*:\s*""database""\s*,\s*""status""\s*:\s*""Healthy""", body);
    }

    [Fact]
    [Trait("AC", "R-006.AC-11")]
    public async Task Health_is_503_when_the_database_is_unreachable()
    {
        await using var container = await ContainerRuntime.StartPostgresAsync(PostgresFixture.NewPostgres());
        using var host = new IntegrationHost(PostgresFixture.ConnectionString(container, "aihub_it"));
        var client = host.CreateClient();

        await container.StopAsync();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
