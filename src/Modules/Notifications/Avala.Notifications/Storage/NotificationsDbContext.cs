using Avala.Notifications.Contracts;
using Avala.Notifications.Dispatching;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Notifications.Storage;

internal sealed class NotificationsDbContext(string database) : DbContext
{
    public DbSet<StoredSent> Sent => Set<StoredSent>();

    public DbSet<StoredRecord> Records => Set<StoredRecord>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredSent>(sent =>
        {
            sent.ToTable("Sent");
            sent.HasKey(row => row.Key);
            sent.Property(row => row.Key).ValueGeneratedOnAdd();
            sent.HasIndex(row => row.Handle);
        });
        modelBuilder.Entity<StoredRecord>(record =>
        {
            record.ToTable("Records");
            record.HasKey(row => row.Key);
            record.Property(row => row.Key).ValueGeneratedOnAdd();
        });
    }
}

internal sealed class StoredSent
{
    public int Key { get; init; }

    public string Subject { get; init; } = string.Empty;

    public string Handle { get; init; } = string.Empty;

    public long At { get; init; }

    public static StoredSent Of(SentSubject sent) => new() { Subject = sent.Subject, Handle = sent.Handle, At = sent.At.UtcTicks };

    public SentSubject Read() => new(Subject, Handle, new DateTimeOffset(At, TimeSpan.Zero));
}

internal sealed class StoredRecord
{
    public int Key { get; init; }

    public long At { get; init; }

    public string Fact { get; init; } = string.Empty;

    public static StoredRecord Of(NotificationRecord record) => new() { At = record.At.UtcTicks, Fact = StoredJson.Write(record) };

    public NotificationRecord Read() => StoredJson.Read<NotificationRecord>(Fact);
}
