using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Intelligence.Data.Migrations;

[DbContext(typeof(IntelligenceDbContext))]
[Migration("20261016000000_CreateIntelligenceSchema")]
public sealed class CreateIntelligenceSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(IntelligenceDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
