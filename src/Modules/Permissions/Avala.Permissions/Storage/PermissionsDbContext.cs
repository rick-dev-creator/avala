using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;
using Microsoft.EntityFrameworkCore;

namespace Avala.Permissions.Storage;

internal enum FactKind
{
    Policy,
    Autonomy,
    Decision,
    Form,
    Answer,
    Ended,
}

internal sealed class StoredFact
{
    public int Key { get; init; }

    public string Kind { get; init; } = string.Empty;

    public Guid Session { get; init; }

    public Guid Job { get; init; }

    public long At { get; init; }

    public string Fact { get; init; } = string.Empty;

    public static StoredFact Of<T>(FactKind kind, SessionId session, Option<JobId> job, DateTimeOffset at, T fact) => new()
    {
        Kind = kind.ToString(),
        Session = session.Value,
        Job = job.Match(found => found.Value, () => Guid.Empty),
        At = at.UtcTicks,
        Fact = StoredJson.Write(fact),
    };

    public T Read<T>() => StoredJson.Read<T>(Fact);
}

internal sealed class PermissionsDbContext(string database) : DbContext
{
    public DbSet<StoredFact> Facts => Set<StoredFact>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<StoredFact>(fact =>
        {
            fact.ToTable("Facts");
            fact.HasKey(row => row.Key);
            fact.Property(row => row.Key).ValueGeneratedOnAdd();
            fact.HasIndex(row => row.Job);
        });
}
