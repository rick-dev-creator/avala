using Microsoft.EntityFrameworkCore;

namespace Avala.Documentation.Storage;

internal sealed class DocumentationDbContext(string database) : DbContext
{
    public DbSet<StoredChoice> Choices => Set<StoredChoice>();

    public DbSet<StoredVersion> Versions => Set<StoredVersion>();

    public DbSet<StoredWriter> Writers => Set<StoredWriter>();

    public DbSet<StoredRequest> Requests => Set<StoredRequest>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredChoice>(choice =>
        {
            choice.ToTable("Choices");
            choice.HasKey(row => row.Job);
        });
        modelBuilder.Entity<StoredVersion>(version =>
        {
            version.ToTable("Versions");
            version.HasKey(row => new { row.Job, row.Number });
        });
        modelBuilder.Entity<StoredWriter>(writer =>
        {
            writer.ToTable("Writers");
            writer.HasKey(row => row.Writer);
        });
        modelBuilder.Entity<StoredRequest>(request =>
        {
            request.ToTable("Requests");
            request.HasKey(row => new { row.Job, row.Number });
        });
    }
}
