using Avala.Server.Activity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal sealed class ActivityDbContext(DbContextOptions<ActivityDbContext> options) : DbContext(options)
{
    public const string Schema = "activity";

    public DbSet<FleetJob> Jobs => Set<FleetJob>();

    public DbSet<FleetInstance> Instances => Set<FleetInstance>();

    public DbSet<ReceivedEvent> ReceivedEvents => Set<ReceivedEvent>();

    public DbSet<QuarterUsage> Quarters => Set<QuarterUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ActivityDbContext).Assembly);
    }
}
