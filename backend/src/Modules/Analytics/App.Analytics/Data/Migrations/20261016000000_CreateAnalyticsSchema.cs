using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Analytics.Data.Migrations;

[DbContext(typeof(AnalyticsDbContext))]
[Migration("20261016000000_CreateAnalyticsSchema")]
public sealed class CreateAnalyticsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(AnalyticsDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
