using System.Reflection;

namespace App.IntegrationTests;

// The nine modules, the schema each owns (its name in lower case) and the migrations its
// implementation assembly carries — read from EF Core's [Migration] attribute by name.
public static class ModuleSchemas
{
    public const string HistoryTable = "__ef_migrations_history";
    private const string MigrationAttribute = "Microsoft.EntityFrameworkCore.Migrations.MigrationAttribute";

    public static readonly string[] Modules =
    [
        "Discovery", "Connections", "Capture", "Processing", "Intelligence",
        "Recommendations", "Knowledge", "Analytics", "Governance",
    ];

    public static string Schema(string module) => module.ToLowerInvariant();

    public static IReadOnlyList<string> Migrations(string module) =>
        Assembly.Load(new AssemblyName($"App.{module}"))
            .GetTypes()
            .SelectMany(type => type.GetCustomAttributesData())
            .Where(attribute => attribute.AttributeType.FullName == MigrationAttribute)
            .Select(attribute => (string)attribute.ConstructorArguments[0].Value!)
            .Order(StringComparer.Ordinal)
            .ToList();

    // Every table outside PostgreSQL's own schemas, as "schema.table".
    public static async Task<IReadOnlyList<string>> TablesAsync(PostgresFixture postgres, string database) =>
        (await postgres.QueryAsync(database,
            "SELECT table_schema || '.' || table_name FROM information_schema.tables " +
            "WHERE table_schema NOT IN ('pg_catalog', 'information_schema') AND table_schema NOT LIKE 'pg_%' " +
            "ORDER BY 1;"))
        .Select(row => row[0])
        .ToList();

    public static async Task<IReadOnlyList<string>> SchemasAsync(PostgresFixture postgres, string database) =>
        (await postgres.QueryAsync(database, "SELECT schema_name FROM information_schema.schemata ORDER BY 1;"))
        .Select(row => row[0])
        .ToList();

    // The migration ids a schema's history table lists, or null when it has no history table.
    public static async Task<IReadOnlyList<string>?> HistoryAsync(PostgresFixture postgres, string database, string schema)
    {
        var column = await postgres.QueryAsync(database,
            "SELECT column_name FROM information_schema.columns " +
            $"WHERE table_schema = '{schema}' AND table_name = '{HistoryTable}' " +
            "AND replace(lower(column_name), '_', '') = 'migrationid';");
        if (column.Count == 0)
            return null;
        return (await postgres.QueryAsync(database,
                $"SELECT \"{column[0][0]}\" FROM \"{schema}\".\"{HistoryTable}\" ORDER BY 1;"))
            .Select(row => row[0])
            .ToList();
    }
}
