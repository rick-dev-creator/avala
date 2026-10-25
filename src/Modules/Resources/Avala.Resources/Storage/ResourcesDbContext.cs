using Microsoft.EntityFrameworkCore;

namespace Avala.Resources.Storage;

internal sealed class ResourcesDbContext(string database) : DbContext
{
    public DbSet<StoredWorktreeEvent> Events => Set<StoredWorktreeEvent>();

    public DbSet<StoredWorktreeDay> Days => Set<StoredWorktreeDay>();

    public DbSet<StoredSeenWorktree> Seen => Set<StoredSeenWorktree>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredWorktreeEvent>(row =>
        {
            row.ToTable("WorktreeEvents");
            row.HasKey(stored => stored.Key);
            row.Property(stored => stored.Key).ValueGeneratedOnAdd();
            row.HasIndex(stored => stored.At);
        });
        modelBuilder.Entity<StoredWorktreeDay>(row =>
        {
            row.ToTable("WorktreeDays");
            row.HasKey(stored => stored.Day);
        });
        modelBuilder.Entity<StoredSeenWorktree>(row =>
        {
            row.ToTable("SeenWorktrees");
            row.HasKey(stored => stored.Path);
        });
    }
}
