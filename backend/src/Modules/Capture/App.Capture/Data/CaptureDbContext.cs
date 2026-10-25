using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Capture.Data;

// The Capture module's data, in the `capture` schema.
public sealed class CaptureDbContext(DbContextOptions<CaptureDbContext> options)
    : ModuleDbContext<CaptureDbContext>(options), IModuleSchema
{
    public static string Schema => "capture";

    public DbSet<StoredUsageEvent> UsageEvents => Set<StoredUsageEvent>();

    public DbSet<HandOverCursor> HandOverCursors => Set<HandOverCursor>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<StoredUsageEvent>(entity =>
        {
            entity.ToTable(StoredUsageEvent.Table);
            entity.HasKey(e => e.Id).HasName("pk_usage_events");
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(e => e.Position).HasColumnName("position").ValueGeneratedOnAdd().UseIdentityByDefaultColumn();
            entity.HasIndex(e => e.Position).IsUnique().HasDatabaseName("ix_usage_events_position");
            entity.Property(e => e.Tool).HasColumnName("tool");
            entity.Property(e => e.Employee).HasColumnName("employee");
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        });
        modelBuilder.Entity<HandOverCursor>(entity =>
        {
            entity.ToTable(HandOverCursor.Table);
            entity.HasKey(e => e.Consumer).HasName("pk_hand_over_cursors");
            entity.Property(e => e.Consumer).HasColumnName("consumer");
            entity.Property(e => e.Position).HasColumnName("position");
        });
    }
}

// One appended usage event. Position is its place in store order, given by the database;
// the row is never updated or deleted.
public sealed class StoredUsageEvent
{
    public const string Table = "usage_events";

    public Guid Id { get; init; }

    public long Position { get; init; }

    public required string Tool { get; init; }

    public required string Employee { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public required string Kind { get; init; }

    public required string Payload { get; init; }
}

// How far in store order the worker has handed events to a consumer.
public sealed class HandOverCursor
{
    public const string Table = "hand_over_cursors";

    public required string Consumer { get; init; }

    public long Position { get; set; }
}
