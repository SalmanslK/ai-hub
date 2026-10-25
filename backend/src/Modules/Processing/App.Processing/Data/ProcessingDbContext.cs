using App.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace App.Processing.Data;

// The Processing module's data, in the `processing` schema.
public sealed class ProcessingDbContext(DbContextOptions<ProcessingDbContext> options)
    : ModuleDbContext<ProcessingDbContext>(options), IModuleSchema
{
    public static string Schema => "processing";

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable(ProcessedEvent.Table);
            entity.HasKey(e => e.EventId).HasName("pk_processed_events");
            entity.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedNever();
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
        });
    }
}

// One usage event Processing has handled; its key is what makes a redelivery a no-op.
public sealed class ProcessedEvent
{
    public const string Table = "processed_events";

    public Guid EventId { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
