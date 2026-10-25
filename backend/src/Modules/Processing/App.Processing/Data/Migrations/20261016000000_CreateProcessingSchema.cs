using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Processing.Data.Migrations;

[DbContext(typeof(ProcessingDbContext))]
[Migration("20261016000000_CreateProcessingSchema")]
public sealed class CreateProcessingSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(ProcessingDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
