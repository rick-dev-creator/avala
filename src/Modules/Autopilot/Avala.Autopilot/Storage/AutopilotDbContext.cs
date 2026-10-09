using Microsoft.EntityFrameworkCore;

namespace Avala.Autopilot.Storage;

internal sealed class AutopilotDbContext(string database) : DbContext
{
    public DbSet<StoredTask> Tasks => Set<StoredTask>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredTask>(task =>
        {
            task.ToTable("Tasks");
            task.HasKey(row => row.Key);
            task.Property(row => row.Key).ValueGeneratedOnAdd();
            task.HasIndex(row => new { row.Repository, row.Source, row.TaskKey }).IsUnique();
        });
}
