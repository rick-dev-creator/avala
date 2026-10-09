using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Avala.Fixtures.Violating.Infrastructure;

public sealed class LedgerEntry
{
    public int Key { get; init; }

    public string Text { get; init; } = string.Empty;
}

public sealed class DriftedLedgerDbContext(string database) : DbContext
{
    public DbSet<LedgerEntry> Entries => Set<LedgerEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<LedgerEntry>().HasKey(entry => entry.Key);
}

[DbContext(typeof(DriftedLedgerDbContext))]
[Migration("20260101000000_Initial")]
public sealed class InitialLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(DriftedLedgerDbContext))]
public sealed class DriftedLedgerDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
    }
}
