using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace App.Tests.Host;

// The real host, in process, with every log entry it writes captured in Logs.
// Logs is registered as one more logger provider, so it receives what any sink would.
// The fast suite has no database: migrations at start-up are off and the `database` health
// check is dropped; the integration tests (App.IntegrationTests) cover both on a real one.
public sealed class TestHost(Action<IWebHostBuilder>? configure = null) : WebApplicationFactory<Program>
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string MigrateAtStartupSetting = "Database:MigrateAtStartup";
    public const string DatabaseCheck = "database";

    public LogCapture Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { [MigrateAtStartupSetting] = "false" }));
        builder.ConfigureServices(services => services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            foreach (var registration in options.Registrations.Where(r => r.Name == DatabaseCheck).ToList())
                options.Registrations.Remove(registration);
        }));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        configure?.Invoke(builder);
    }

    public static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
}
