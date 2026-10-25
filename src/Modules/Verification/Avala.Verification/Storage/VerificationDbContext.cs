using System.Text.Json.Nodes;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;
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

    public VerificationReport Read()
    {
        var report = StoredJson.Read<VerificationReport>(Report);

        return report.Outcome == VerificationOutcome.NoChecksDeclared && !Says(nameof(VerificationReport.Missing)) ? report with { Missing = true } : report;
    }

    private bool Says(string property) =>
        JsonNode.Parse(Report) is JsonObject stored && stored.Any(pair => string.Equals(pair.Key, property, StringComparison.OrdinalIgnoreCase));
}

internal sealed class StoredDiscovery
{
    public Guid Job { get; init; }

    public string Repository { get; init; } = string.Empty;

    public int State { get; set; }

    public Guid Scout { get; set; }

    public Guid Follows { get; set; }

    public string Declaration { get; set; } = string.Empty;

    public string Proposal { get; set; } = string.Empty;

    public bool Reads { get; set; }

    public string Ran { get; set; } = string.Empty;

    public bool Final { get; set; }

    public long At { get; set; }

    public static StoredDiscovery Of(Discovery discovery) => new()
    {
        Job = discovery.Job.Value,
        Repository = discovery.Repository,
        State = (int)discovery.State,
        Scout = discovery.Scout.Match(scout => scout.Value, () => Guid.Empty),
        Follows = discovery.Follows.Match(owner => owner.Value, () => Guid.Empty),
        Declaration = discovery.Declaration,
        Proposal = discovery.Proposal,
        Reads = discovery.Reads,
        Ran = discovery.Ran,
        Final = discovery.Final,
        At = discovery.At.UtcTicks,
    };

    public Discovery Read() => new(new JobId(Job), Repository, (DiscoveryState)State, new DateTimeOffset(At, TimeSpan.Zero))
    {
        Scout = Scout == Guid.Empty ? Option<JobId>.None : new JobId(Scout),
        Follows = Follows == Guid.Empty ? Option<JobId>.None : new JobId(Follows),
        Declaration = Declaration,
        Proposal = Proposal,
        Reads = Reads,
        Ran = Ran,
        Final = Final,
    };
}

internal sealed class VerificationDbContext(string database) : DbContext
{
    public DbSet<StoredReport> Reports => Set<StoredReport>();

    public DbSet<StoredDiscovery> Discoveries => Set<StoredDiscovery>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredReport>(report =>
        {
            report.ToTable("Reports");
            report.HasKey(row => row.Key);
            report.Property(row => row.Key).ValueGeneratedOnAdd();
            report.HasIndex(row => row.Job);
        })
        .Entity<StoredDiscovery>(discovery =>
        {
            discovery.ToTable("Discoveries");
            discovery.HasKey(row => row.Job);
            discovery.Property(row => row.Job).ValueGeneratedNever();
        });
}
