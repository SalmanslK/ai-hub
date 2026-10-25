using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Recommendations.Data.Migrations;

[DbContext(typeof(RecommendationsDbContext))]
[Migration("20261016000000_CreateRecommendationsSchema")]
public sealed class CreateRecommendationsSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.EnsureSchema(RecommendationsDbContext.Schema);

    // The schema also holds the module's migration history, so going down leaves it in place.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
