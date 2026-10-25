using App.Connections.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace App.Connections;

// Connections' connectors and the usage the simulated one reads; registered by the host.
public static class ConnectionsServices
{
    public static IServiceCollection AddConnectors(this IServiceCollection services)
    {
        services.AddSingleton(SimulatedUsage.Sample());
        services.AddScoped<IConnector, SimulatedConnector>();
        return services;
    }
}
