using Avala.Delegation.Contracts;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Delegation.Storage;

internal sealed class StoredRecord
{
    public int Key { get; init; }

    public Guid Session { get; init; }

    public string Item { get; init; } = string.Empty;

    public Guid Parent { get; init; }

    public Guid Child { get; init; }

    public long At { get; init; }

    public string Record { get; init; } = string.Empty;

    public static StoredRecord Of(DelegationRecord record) => new()
    {
        Session = record.Session.Value,
        Item = record.Item.Value,
        Parent = record.Parent.Match(parent => parent.Value, () => Guid.Empty),
        Child = record.Child.Match(child => child.Value, () => Guid.Empty),
        At = record.At.UtcTicks,
        Record = StoredJson.Write(record),
    };

    public DelegationRecord Read() => StoredJson.Read<DelegationRecord>(Record);
}

internal sealed class DelegationDbContext(string database) : DbContext
{
    public DbSet<StoredRecord> Records => Set<StoredRecord>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredRecord>(record =>
        {
            record.ToTable("Records");
            record.HasKey(row => row.Key);
            record.Property(row => row.Key).ValueGeneratedOnAdd();
            record.HasIndex(row => new { row.Session, row.Item });
            record.HasIndex(row => row.Parent);
            record.HasIndex(row => row.Child);
        });
}
