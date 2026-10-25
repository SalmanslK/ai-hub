using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace App.Capture.Data.Migrations;

[DbContext(typeof(CaptureDbContext))]
[Migration("20261016120000_CreateUsageEvents")]
public sealed class CreateUsageEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: StoredUsageEvent.Table,
            schema: CaptureDbContext.Schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                position = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                tool = table.Column<string>(type: "text", nullable: false),
                employee = table.Column<string>(type: "text", nullable: false),
                occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                kind = table.Column<string>(type: "text", nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_usage_events", x => x.id));

        migrationBuilder.CreateIndex(
            name: "ix_usage_events_position",
            schema: CaptureDbContext.Schema,
            table: StoredUsageEvent.Table,
            column: "position",
            unique: true);

        migrationBuilder.CreateTable(
            name: HandOverCursor.Table,
            schema: CaptureDbContext.Schema,
            columns: table => new
            {
                consumer = table.Column<string>(type: "text", nullable: false),
                position = table.Column<long>(type: "bigint", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_hand_over_cursors", x => x.consumer));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: HandOverCursor.Table, schema: CaptureDbContext.Schema);
        migrationBuilder.DropTable(name: StoredUsageEvent.Table, schema: CaptureDbContext.Schema);
    }
}
