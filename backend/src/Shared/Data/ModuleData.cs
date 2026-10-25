using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Shared.Data;

// One module's data context, as the host registered it; the host migrates each in turn at start-up.
public sealed record ModuleDataRegistration(Type ContextType);

public static class ModuleData
{
    // Each module keeps its migration history in its own schema, so no history lists another module's migration.
    public const string HistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services,
        Func<IServiceProvider, string> connectionString)
        where TContext : ModuleDbContext<TContext>, IModuleSchema
    {
        services.AddDbContext<TContext>((provider, options) => options.UseNpgsql(connectionString(provider),
            npgsql => npgsql.MigrationsHistoryTable(HistoryTable, TContext.Schema)));
        services.AddSingleton(new ModuleDataRegistration(typeof(TContext)));
        return services;
    }
}
