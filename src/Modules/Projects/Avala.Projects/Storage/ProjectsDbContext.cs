using Microsoft.EntityFrameworkCore;

namespace Avala.Projects.Storage;

internal sealed class ProjectsDbContext(string database) : DbContext
{
    public DbSet<StoredProject> Projects => Set<StoredProject>();

    public DbSet<StoredPlacement> Placements => Set<StoredPlacement>();

    public DbSet<StoredArchive> Archived => Set<StoredArchive>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredProject>(project =>
        {
            project.ToTable("Projects");
            project.HasKey(row => row.Id);
        });
        modelBuilder.Entity<StoredPlacement>(placement =>
        {
            placement.ToTable("Placements");
            placement.HasKey(row => row.Job);
        });
        modelBuilder.Entity<StoredArchive>(archive =>
        {
            archive.ToTable("Archived");
            archive.HasKey(row => row.Job);
        });
    }
}
