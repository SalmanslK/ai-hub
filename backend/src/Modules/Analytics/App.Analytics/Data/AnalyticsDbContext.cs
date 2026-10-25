using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Analytics.Data;

// The Analytics module's data, in the `analytics` schema.
public sealed class AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options)
    : ModuleDbContext<AnalyticsDbContext>(options), IModuleSchema
{
    public static string Schema => "analytics";
}
