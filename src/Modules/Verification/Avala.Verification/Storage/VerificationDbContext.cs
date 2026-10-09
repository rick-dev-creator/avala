using Avala.Storage;
using Avala.Verification.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Avala.Verification.Storage;

internal sealed class StoredReport
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public int Attempt { get; init; }

    public long At { get; init; }

    public string Report { get; init; } = string.Empty;

    public static StoredReport Of(VerificationReport report) => new()
    {
        Job = report.Job.Value,
        Attempt = report.Attempt,
        At = report.VerifiedAt.UtcTicks,
        Report = StoredJson.Write(report),
    };

    public VerificationReport Read() => StoredJson.Read<VerificationReport>(Report);
}

internal sealed class VerificationDbContext(string database) : DbContext
{
    public DbSet<StoredReport> Reports => Set<StoredReport>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredReport>(report =>
        {
            report.ToTable("Reports");
            report.HasKey(row => row.Key);
            report.Property(row => row.Key).ValueGeneratedOnAdd();
            report.HasIndex(row => row.Job);
        });
}
