using App.Connections;
using App.Connections.Contracts;
using App.IntegrationTests.EventPipeline;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace App.IntegrationTests.Connections;

// The real host, in process, on one database, with the simulated connector's usage replaced by `usage`.
// With `worker: false` Capture's background worker is taken out, so nothing reaches Processing.
public sealed class ConnectorHost(string connectionString, SimulatedUsage usage, bool worker = true)
    : WebApplicationFactory<Program>
{
    private const string CaptureAssembly = "App.Capture";

    public LevelledLogs Logs { get; } = new();

    public int UsageReplaced { get; private set; }

    public int WorkersRemoved { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { [IntegrationHost.ConnectionSetting] = connectionString }));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureTestServices(services =>
        {
            var registered = services.Where(d => d.ServiceType == typeof(SimulatedUsage)).ToList();
            foreach (var descriptor in registered)
                services.Remove(descriptor);
            UsageReplaced = registered.Count;
            services.AddSingleton(usage);
            if (!worker)
                WorkersRemoved = RemoveCaptureWorkers(services);
        });
    }

    public ConnectorHost Started()
    {
        _ = Services;
        Assert.True(UsageReplaced > 0, "The host registers no SimulatedUsage for the simulated connector.");
        if (!worker)
            Assert.True(WorkersRemoved > 0, "Capture registers no hosted worker to take out.");
        return this;
    }

    // Runs the simulated connector the host registered, in a scope of its own.
    public async Task<ConnectorRunResult> RunSimulatedConnectorAsync()
    {
        using var scope = Services.CreateScope();
        var connector = Assert.Single(scope.ServiceProvider.GetServices<IConnector>().OfType<SimulatedConnector>());
        return await connector.RunAsync(CancellationToken.None);
    }

    private static int RemoveCaptureWorkers(IServiceCollection services)
    {
        var workers = services
            .Where(d => d.ServiceType == typeof(IHostedService)
                        && (d.ImplementationType ?? d.ImplementationInstance?.GetType())?.Assembly.GetName().Name == CaptureAssembly)
            .ToList();
        foreach (var descriptor in workers)
            services.Remove(descriptor);
        return workers.Count;
    }
}
