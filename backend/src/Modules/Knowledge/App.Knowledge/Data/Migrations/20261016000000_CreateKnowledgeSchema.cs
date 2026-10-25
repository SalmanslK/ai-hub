using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Knowledge.Data.Migrations;

[DbContext(typeof(KnowledgeDbContext))]
[Migration("20261016000000_CreateKnowledgeSchema")]
public sealed class CreateKnowledgeSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(KnowledgeDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
