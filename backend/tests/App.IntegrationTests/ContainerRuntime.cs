using Testcontainers.PostgreSql;

namespace App.IntegrationTests;

// Starts PostgreSQL in a container. Without a container runtime it throws, so every test that needs
// the database fails — a run without Docker must never look like a pass or a skip (plan §7).
public static class ContainerRuntime
{
    public const string Missing =
        "The container runtime is missing or not running: the integration tests need Docker (or a compatible " +
        "runtime) to start PostgreSQL, and they fail rather than skip without one.";

    public static async Task<PostgreSqlContainer> StartPostgresAsync(PostgreSqlBuilder builder)
    {
        PostgreSqlContainer? container = null;
        try
        {
            container = builder.Build();
            await container.StartAsync();
            return container;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            if (container is not null)
            {
                try
                {
                    await container.DisposeAsync();
                }
                catch (Exception)
                {
                    // The runtime that failed to start it cannot remove it either; the original error is the one to report.
                }
            }
            throw new ContainerRuntimeMissingException($"{Missing} ({error.GetType().Name}: {error.Message})", error);
        }
    }
}

public sealed class ContainerRuntimeMissingException(string message, Exception inner) : Exception(message, inner);
