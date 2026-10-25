using System.Data.Common;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Xunit;

namespace App.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}

// One PostgreSQL container for the collection; each test takes a database of its own, so no test
// sees another's schemas. Queries run through psql inside the container.
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:17-alpine";
    public const string User = "aihub_it_user";
    public const string Password = "it-Pa55word-7f3c9e1b";
    private const string AdminDatabase = "aihub_it";

    private PostgreSqlContainer? _container;

    public PostgreSqlContainer Container =>
        _container ?? throw new InvalidOperationException("The PostgreSQL container has not started.");

    public static PostgreSqlBuilder NewPostgres() =>
        new PostgreSqlBuilder(Image)
            .WithUsername(User)
            .WithPassword(Password)
            .WithDatabase(AdminDatabase);

    public async Task InitializeAsync() => _container = await ContainerRuntime.StartPostgresAsync(NewPostgres());

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    // A new, empty database; returns its name.
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "it_" + Guid.NewGuid().ToString("N");
        await QueryAsync(Container, AdminDatabase, $"CREATE DATABASE {name};");
        return name;
    }

    public string ConnectionString(string database) => ConnectionString(Container, database);

    public static string ConnectionString(PostgreSqlContainer container, string database)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = container.GetConnectionString() };
        builder["Database"] = database;
        return builder.ConnectionString;
    }

    public Task<IReadOnlyList<string[]>> QueryAsync(string database, string sql) => QueryAsync(Container, database, sql);

    // One string array per row, the columns in query order.
    public static async Task<IReadOnlyList<string[]>> QueryAsync(IContainer container, string database, string sql)
    {
        var result = await container.ExecAsync(
            ["psql", "-U", User, "-d", database, "-v", "ON_ERROR_STOP=1", "-A", "-t", "-F", "\t", "-c", sql]);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"psql failed ({result.ExitCode}): {result.Stderr}");
        return result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .ToList();
    }
}
