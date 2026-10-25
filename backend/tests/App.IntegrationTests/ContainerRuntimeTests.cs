using System.Reflection;
using Xunit;

namespace App.IntegrationTests;

// Outside the PostgreSQL collection, so it runs whether or not a container runtime is present.
public sealed class ContainerRuntimeTests
{
    [Fact]
    [Trait("AC", "R-005.AC-7")]
    public async Task Starting_postgres_without_a_container_runtime_fails_naming_the_runtime()
    {
        // Nothing listens on port 1: the runtime this builder points at is missing.
        var builder = PostgresFixture.NewPostgres().WithDockerEndpoint("tcp://127.0.0.1:1");

        var error = await Assert.ThrowsAsync<ContainerRuntimeMissingException>(
            () => ContainerRuntime.StartPostgresAsync(builder));

        Assert.Contains("container runtime is missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("AC", "R-005.AC-7")]
    public void The_database_tests_start_postgres_in_their_fixture_and_none_can_be_skipped()
    {
        // The collection fixture starts the container; when it throws, xUnit fails every test in the collection.
        Assert.Contains(typeof(ICollectionFixture<PostgresFixture>), typeof(PostgresCollection).GetInterfaces());
        var collection = typeof(DatabaseTests).GetCustomAttributesData()
            .SingleOrDefault(a => a.AttributeType == typeof(CollectionAttribute));
        Assert.Equal(PostgresCollection.Name, collection?.ConstructorArguments[0].Value);

        var skippable = typeof(ContainerRuntimeTests).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods())
            .Where(method => method.GetCustomAttribute<FactAttribute>() is { Skip: not null })
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();
        Assert.True(skippable.Count == 0, "Tests marked to skip: " + string.Join(", ", skippable));
    }
}
