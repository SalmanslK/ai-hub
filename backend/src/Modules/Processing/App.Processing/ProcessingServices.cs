using App.Processing.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace App.Processing;

// What Processing offers other modules, registered by the host.
public static class ProcessingServices
{
    public static IServiceCollection AddProcessing(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IUsageEventProcessor, UsageEventProcessor>();
        return services;
    }
}
