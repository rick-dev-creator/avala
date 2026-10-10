using Microsoft.EntityFrameworkCore;

namespace Avala.Handoffs.Storage;

internal sealed class HandoffsDbContext(string database) : DbContext
{
    public DbSet<StoredHandoff> Handoffs => Set<StoredHandoff>();

    public DbSet<StoredWait> Waits => Set<StoredWait>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredHandoff>(handoff =>
        {
            handoff.ToTable("Handoffs");
            handoff.HasKey(row => row.Key);
            handoff.Property(row => row.Key).ValueGeneratedOnAdd();
            handoff.HasIndex(row => row.Job);
        });
        modelBuilder.Entity<StoredWait>(wait =>
        {
            wait.ToTable("Waits");
            wait.HasKey(row => row.Job);
        });
    }
}
