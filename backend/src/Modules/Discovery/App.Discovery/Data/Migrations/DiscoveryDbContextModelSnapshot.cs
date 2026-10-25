using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace App.Discovery.Data.Migrations;

[DbContext(typeof(DiscoveryDbContext))]
public sealed class DiscoveryDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder
            .HasDefaultSchema(DiscoveryDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
}
