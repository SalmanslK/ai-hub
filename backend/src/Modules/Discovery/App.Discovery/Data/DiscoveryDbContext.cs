using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Discovery.Data;

// The Discovery module's data, in the `discovery` schema.
public sealed class DiscoveryDbContext(DbContextOptions<DiscoveryDbContext> options)
    : ModuleDbContext<DiscoveryDbContext>(options), IModuleSchema
{
    public static string Schema => "discovery";
}
