using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Intelligence.Data;

// The Intelligence module's data, in the `intelligence` schema.
public sealed class IntelligenceDbContext(DbContextOptions<IntelligenceDbContext> options)
    : ModuleDbContext<IntelligenceDbContext>(options), IModuleSchema
{
    public static string Schema => "intelligence";
}
