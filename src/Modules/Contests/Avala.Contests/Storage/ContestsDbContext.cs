using Microsoft.EntityFrameworkCore;

namespace Avala.Contests.Storage;

internal sealed class ContestsDbContext(string database) : DbContext
{
    public DbSet<StoredContest> Contests => Set<StoredContest>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredContest>(contest =>
        {
            contest.ToTable("Contests");
            contest.HasKey(row => row.Id);
            contest.HasIndex(row => row.Started);
        });
}
