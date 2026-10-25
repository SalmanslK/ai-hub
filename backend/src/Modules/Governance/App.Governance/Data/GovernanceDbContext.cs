using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Governance.Data;

// The Governance module's data, in the `governance` schema.
public sealed class GovernanceDbContext(DbContextOptions<GovernanceDbContext> options)
    : ModuleDbContext<GovernanceDbContext>(options), IModuleSchema
{
    public static string Schema => "governance";
}
