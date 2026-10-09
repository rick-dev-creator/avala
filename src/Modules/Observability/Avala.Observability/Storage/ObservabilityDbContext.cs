using Microsoft.EntityFrameworkCore;

namespace Avala.Observability.Storage;

internal sealed class ObservabilityDbContext(string database) : DbContext
{
    public DbSet<StoredSession> Sessions => Set<StoredSession>();

    public DbSet<StoredFact> Facts => Set<StoredFact>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredSession>(session =>
        {
            session.ToTable("UsageSessions");
            session.HasKey(row => row.Session);
        });

        modelBuilder.Entity<StoredFact>(fact =>
        {
            fact.ToTable("UsageFacts");
            fact.HasKey(row => row.Key);
            fact.Property(row => row.Key).ValueGeneratedOnAdd();
            fact.HasIndex(row => row.At);
        });
    }
}
