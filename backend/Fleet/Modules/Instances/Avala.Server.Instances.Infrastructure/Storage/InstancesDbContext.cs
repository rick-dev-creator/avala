using Avala.Server.Instances.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Instances.Infrastructure.Storage;

internal sealed class InstancesDbContext(DbContextOptions<InstancesDbContext> options) : DbContext(options)
{
    public const string Schema = "instances";

    public DbSet<Instance> Instances => Set<Instance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InstancesDbContext).Assembly);
    }
}
