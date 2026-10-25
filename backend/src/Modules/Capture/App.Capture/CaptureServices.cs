using App.Capture.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace App.Capture;

// What Capture offers other modules, and the worker that hands its events to Processing; registered by the host.
public static class CaptureServices
{
    public static IServiceCollection AddCapture(this IServiceCollection services)
    {
        services.AddScoped<IUsageEventLog, UsageEventLog>();
        services.AddHostedService<UsageEventWorker>();
        return services;
    }
}
