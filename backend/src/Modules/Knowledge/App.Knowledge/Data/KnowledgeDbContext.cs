using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Knowledge.Data;

// The Knowledge module's data, in the `knowledge` schema.
public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options)
    : ModuleDbContext<KnowledgeDbContext>(options), IModuleSchema
{
    public static string Schema => "knowledge";
}
