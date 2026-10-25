using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Connections.Data.Migrations;

[DbContext(typeof(ConnectionsDbContext))]
[Migration("20261016000000_CreateConnectionsSchema")]
public sealed class CreateConnectionsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(ConnectionsDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
