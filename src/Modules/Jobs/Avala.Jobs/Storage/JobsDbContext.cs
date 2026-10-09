using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avala.Jobs.Storage;

internal sealed class JobsDbContext(string database) : DbContext
{
    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<StoredChoice> Choices => Set<StoredChoice>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(Configure);
        modelBuilder.Entity<StoredChoice>(choice =>
        {
            choice.ToTable("ConnectionChoices");
            choice.HasKey(row => row.Key);
            choice.Property(row => row.Key).ValueGeneratedOnAdd();
            choice.HasIndex(row => row.Job);
        });
    }

    private static void Configure(EntityTypeBuilder<Job> job)
    {
        job.ToTable("Jobs");
        job.HasKey(entity => entity.Id);
        job.Property(entity => entity.Id).HasConversion(id => id.Value, value => new JobId(value));
        job.Property(entity => entity.Instruction).HasConversion(instruction => instruction.Text, text => Stored.Instruction(text));
        job.Property(entity => entity.Budget).HasConversion(budget => budget.AttemptsPerRound, value => Stored.Budget(value));
        job.Property(entity => entity.Repository).HasConversion(repository => repository.Value, value => Stored.Repository(value));
        job.Property(entity => entity.Submitted).HasConversion(at => at.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
        job.Property(entity => entity.State).HasConversion<string>();
        job.Property(entity => entity.Workspace).HasConversion(
            option => option.Match(id => id.Value, () => Guid.Empty),
            value => value == Guid.Empty ? Option<WorkspaceId>.None : Option<WorkspaceId>.Some(new WorkspaceId(value)));
        job.Property(entity => entity.Session).HasConversion(
            option => option.Match(id => id.Value, () => Guid.Empty),
            value => value == Guid.Empty ? Option<SessionId>.None : Option<SessionId>.Some(new SessionId(value)));
        job.Property(entity => entity.Resume).HasConversion(
            option => option.Match(token => token.Value, () => string.Empty),
            text => text.Length == 0 ? Option<ResumeToken>.None : Option<ResumeToken>.Some(new ResumeToken(text)));
        job.Property(entity => entity.Autonomy).HasConversion(
            option => option.Match(level => level.ToString(), () => string.Empty),
            text => text.Length == 0 ? Option<Autonomy>.None : Option<Autonomy>.Some(Enum.Parse<Autonomy>(text)));
        job.Property(entity => entity.Connection).HasConversion(
            option => option.Match(name => name.Value, () => string.Empty),
            text => text.Length == 0 ? Option<ConnectionName>.None : Option<ConnectionName>.Some(new ConnectionName(text)));
        job.Property(entity => entity.Parent).HasConversion(
            option => option.Match(id => id.Value, () => Guid.Empty),
            value => value == Guid.Empty ? Option<JobId>.None : Option<JobId>.Some(new JobId(value)));
        job.OwnsMany(entity => entity.Attempts, Configure);
    }

    private static void Configure(OwnedNavigationBuilder<Job, Attempt> attempt)
    {
        attempt.ToTable("JobAttempts");
        attempt.WithOwner().HasForeignKey("JobId");
        attempt.Property<int>("Key").ValueGeneratedOnAdd();
        attempt.HasKey("Key");
        attempt.Property(entity => entity.Number).HasConversion(number => number.Value, value => new AttemptNumber(value));
        attempt.Property(entity => entity.Origin).HasConversion<string>();
        attempt.Property(entity => entity.Outcome).HasConversion<string>();
        attempt.Property(entity => entity.Guidance).HasConversion(
            option => option.Match(feedback => feedback.Text, () => string.Empty),
            text => text.Length == 0 ? Option<Feedback>.None : Option<Feedback>.Some(Stored.Feedback(text)));
        attempt.Property(entity => entity.Session).HasConversion(
            option => option.Match(id => id.Value, () => Guid.Empty),
            value => value == Guid.Empty ? Option<SessionId>.None : Option<SessionId>.Some(new SessionId(value)));
    }
}
