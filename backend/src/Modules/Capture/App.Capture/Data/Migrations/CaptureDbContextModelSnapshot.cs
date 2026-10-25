using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace App.Capture.Data.Migrations;

[DbContext(typeof(CaptureDbContext))]
public sealed class CaptureDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema(CaptureDbContext.Schema)
            .HasAnnotation("ProductVersion", "10.0.11")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("App.Capture.Data.HandOverCursor", b =>
        {
            b.Property<string>("Consumer")
                .HasColumnType("text")
                .HasColumnName("consumer");

            b.Property<long>("Position")
                .HasColumnType("bigint")
                .HasColumnName("position");

            b.HasKey("Consumer")
                .HasName("pk_hand_over_cursors");

            b.ToTable("hand_over_cursors", "capture");
        });

        modelBuilder.Entity("App.Capture.Data.StoredUsageEvent", b =>
        {
            b.Property<Guid>("Id")
                .HasColumnType("uuid")
                .HasColumnName("id");

            b.Property<string>("Employee")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("employee");

            b.Property<string>("Kind")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("kind");

            b.Property<DateTimeOffset>("OccurredAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("occurred_at");

            b.Property<string>("Payload")
                .IsRequired()
                .HasColumnType("jsonb")
                .HasColumnName("payload");

            b.Property<long>("Position")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("position");

            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<long>("Position"));

            b.Property<string>("Tool")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("tool");

            b.HasKey("Id")
                .HasName("pk_usage_events");

            b.HasIndex("Position")
                .IsUnique()
                .HasDatabaseName("ix_usage_events_position");

            b.ToTable("usage_events", "capture");
        });
    }
}
