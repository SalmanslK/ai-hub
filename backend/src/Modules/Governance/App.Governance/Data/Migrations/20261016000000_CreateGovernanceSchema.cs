using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Governance.Data.Migrations;

[DbContext(typeof(GovernanceDbContext))]
[Migration("20261016000000_CreateGovernanceSchema")]
public sealed class CreateGovernanceSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(GovernanceDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
