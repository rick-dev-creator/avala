using Microsoft.EntityFrameworkCore;

namespace Avala.Budgets.Storage;

internal sealed class BudgetsDbContext(string database) : DbContext
{
    public DbSet<StoredIntervention> Interventions => Set<StoredIntervention>();

    public DbSet<StoredCarve> Carves => Set<StoredCarve>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredIntervention>(intervention =>
        {
            intervention.ToTable("Interventions");
            intervention.HasKey(row => row.Key);
            intervention.Property(row => row.Key).ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<StoredCarve>(carve =>
        {
            carve.ToTable("Carves");
            carve.HasKey(row => row.Key);
            carve.Property(row => row.Key).ValueGeneratedOnAdd();
        });
    }
}
