using Microsoft.EntityFrameworkCore;

namespace App.Shared.Data;

// The schema a module's data lives in: its name in lower case. One per module, never shared.
public interface IModuleSchema
{
    static abstract string Schema { get; }
}

// A module's data context: everything it maps lands in the module's own schema.
// Each module's implementation project derives one; its Contracts never see it.
public abstract class ModuleDbContext<TSelf>(DbContextOptions<TSelf> options) : DbContext(options)
    where TSelf : ModuleDbContext<TSelf>, IModuleSchema
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(TSelf.Schema);
        base.OnModelCreating(modelBuilder);
    }
}
