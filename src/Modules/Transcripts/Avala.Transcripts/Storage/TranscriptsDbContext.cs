using Microsoft.EntityFrameworkCore;

namespace Avala.Transcripts.Storage;

internal sealed class TranscriptsDbContext(string database) : DbContext
{
    public DbSet<StoredFact> Facts => Set<StoredFact>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredFact>(fact =>
        {
            fact.ToTable("Facts");
            fact.HasKey(row => row.Key);
            fact.Property(row => row.Key).ValueGeneratedOnAdd();
            fact.HasIndex(row => new { row.Job, row.Slot });
        });
}
