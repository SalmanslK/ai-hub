using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Recommendations.Data;

// The Recommendations module's data, in the `recommendations` schema.
public sealed class RecommendationsDbContext(DbContextOptions<RecommendationsDbContext> options)
    : ModuleDbContext<RecommendationsDbContext>(options), IModuleSchema
{
    public static string Schema => "recommendations";
}
