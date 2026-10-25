using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Connections.Data;

// The Connections module's data, in the `connections` schema.
public sealed class ConnectionsDbContext(DbContextOptions<ConnectionsDbContext> options)
    : ModuleDbContext<ConnectionsDbContext>(options), IModuleSchema
{
    public static string Schema => "connections";
}
