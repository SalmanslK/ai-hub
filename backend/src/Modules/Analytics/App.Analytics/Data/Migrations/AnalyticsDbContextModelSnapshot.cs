using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace App.Analytics.Data.Migrations;

[DbContext(typeof(AnalyticsDbContext))]
public sealed class AnalyticsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder
            .HasDefaultSchema(AnalyticsDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
}
