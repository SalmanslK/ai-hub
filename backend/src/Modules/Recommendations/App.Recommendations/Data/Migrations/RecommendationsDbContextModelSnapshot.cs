using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace App.Recommendations.Data.Migrations;

[DbContext(typeof(RecommendationsDbContext))]
public sealed class RecommendationsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder
            .HasDefaultSchema(RecommendationsDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
}
