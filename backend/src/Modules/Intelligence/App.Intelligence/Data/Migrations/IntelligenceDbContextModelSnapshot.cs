using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace App.Intelligence.Data.Migrations;

[DbContext(typeof(IntelligenceDbContext))]
public sealed class IntelligenceDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder
            .HasDefaultSchema(IntelligenceDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
}
