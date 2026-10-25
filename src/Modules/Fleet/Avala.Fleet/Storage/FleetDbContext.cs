using Microsoft.EntityFrameworkCore;

namespace Avala.Fleet.Storage;

internal sealed class FleetDbContext(string database) : DbContext
{
    public DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    public DbSet<AnnouncedRow> Announced => Set<AnnouncedRow>();

    public DbSet<RejectionRow> Rejections => Set<RejectionRow>();

    public DbSet<ScopeRow> Scopes => Set<ScopeRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxRow>(outbox =>
        {
            outbox.ToTable("Outbox");
            outbox.HasKey(row => row.Sequence);
            outbox.Property(row => row.Sequence).ValueGeneratedOnAdd();
            outbox.HasIndex(row => row.Id).IsUnique();
            outbox.HasIndex(row => new { row.Scope, row.DueAt });
        });
        modelBuilder.Entity<AnnouncedRow>(announced =>
        {
            announced.ToTable("Announced");
            announced.HasKey(row => new { row.Scope, row.Job });
        });
        modelBuilder.Entity<RejectionRow>(rejection =>
        {
            rejection.ToTable("Rejections");
            rejection.HasKey(row => row.Key);
            rejection.Property(row => row.Key).ValueGeneratedOnAdd();
            rejection.HasIndex(row => row.Scope);
        });
        modelBuilder.Entity<ScopeRow>(scope =>
        {
            scope.ToTable("Scopes");
            scope.HasKey(row => row.Scope);
        });
    }
}

internal sealed class OutboxRow
{
    public long Sequence { get; init; }

    public string Scope { get; init; } = string.Empty;

    public Guid Id { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public long OccurredAt { get; init; }

    public int Attempts { get; set; }

    public long DueAt { get; set; }

    public string DeferredReason { get; set; } = string.Empty;

    public long FirstDeferredAt { get; set; }
}

internal sealed class AnnouncedRow
{
    public string Scope { get; init; } = string.Empty;

    public Guid Job { get; init; }

    public bool Keyless { get; init; }
}

internal sealed class RejectionRow
{
    public long Key { get; init; }

    public string Scope { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public long At { get; init; }
}

internal sealed class ScopeRow
{
    public string Scope { get; init; } = string.Empty;

    public long LastDelivery { get; set; }

    public int Dropped { get; set; }
}
