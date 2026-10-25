using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Discovery.Data.Migrations;

[DbContext(typeof(DiscoveryDbContext))]
[Migration("20261016000000_CreateDiscoverySchema")]
public sealed class CreateDiscoverySchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(DiscoveryDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
