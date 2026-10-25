using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace App.Processing.Data.Migrations;

[DbContext(typeof(ProcessingDbContext))]
public sealed class ProcessingDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema(ProcessingDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("App.Processing.Data.ProcessedEvent", b =>
        {
            b.Property<Guid>("EventId")
                .HasColumnType("uuid")
                .HasColumnName("event_id");

            b.Property<DateTimeOffset>("ProcessedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("processed_at");

            b.HasKey("EventId")
                .HasName("pk_processed_events");

            b.ToTable("processed_events", "processing");
        });
    }
}
