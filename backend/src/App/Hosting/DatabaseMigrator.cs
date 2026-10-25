using App.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.Api.Hosting;

// Applies each module's pending migrations, one module after another, before the host starts
// serving. Applying them again changes nothing, so every start runs it; no migration is applied by hand.
public sealed class DatabaseMigrator(
    IServiceScopeFactory scopes,
    IEnumerable<ModuleDataRegistration> modules,
    IOptions<DatabaseOptions> options,
    ILogger<DatabaseMigrator> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.MigrateAtStartup)
            return;
        foreach (var module in modules)
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(module.ContextType);
            logger.LogInformation("Migrating {DataContext}", module.ContextType.Name);
            await context.Database.MigrateAsync(cancellationToken);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
