using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace App.Api.Hosting;

// The one PostgreSQL database every module's schema lives in. The connection string is
// ConnectionStrings:AppDb; the rest is the "Database" section. Validated at start-up: a missing
// connection string stops the host, and the error names the setting, never its value.
public sealed class DatabaseOptions
{
    public const string Section = "Database";
    public const string ConnectionName = "AppDb";

    [Required(ErrorMessage = "The setting ConnectionStrings:" + ConnectionName + " is required.")]
    public string AppDb { get; set; } = "";

    // Each module's migrations are applied at start-up; off only where no database exists (the fast tests).
    public bool MigrateAtStartup { get; set; } = true;

    public static string ConnectionString(IServiceProvider services) =>
        services.GetRequiredService<IOptions<DatabaseOptions>>().Value.AppDb;
}
