using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace App.Knowledge.Data.Migrations;

[DbContext(typeof(KnowledgeDbContext))]
public sealed class KnowledgeDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder) =>
        modelBuilder
            .HasDefaultSchema(KnowledgeDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);
}
