using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace App.Processing.Data.Migrations;

[DbContext(typeof(ProcessingDbContext))]
[Migration("20261016120000_CreateProcessedEvents")]
public sealed class CreateProcessedEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: ProcessedEvent.Table,
            schema: ProcessingDbContext.Schema,
            columns: table => new
            {
                event_id = table.Column<Guid>(type: "uuid", nullable: false),
                processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_processed_events", x => x.event_id));

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: ProcessedEvent.Table, schema: ProcessingDbContext.Schema);
}
