using Avala.Server.Activity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal sealed class FleetInstanceConfiguration : IEntityTypeConfiguration<FleetInstance>
{
    private const string InstanceColumn = "instance_id";

    public void Configure(EntityTypeBuilder<FleetInstance> builder)
    {
        builder.ToTable("instances");
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.Id).HasColumnName("id");
        builder.Property(instance => instance.Name).HasColumnName("name").HasColumnType("text").IsRequired();
        builder.Property(instance => instance.LastSeenAt).HasColumnName("last_seen_at");
        builder.OwnsOne(instance => instance.Tokens, Columns.MapTokens);
        builder.OwnsMany(instance => instance.Costs, costs =>
        {
            costs.Property<Guid>(InstanceColumn).HasColumnName(InstanceColumn);
            Columns.MapCosts(costs, "instance_costs", InstanceColumn);
        });
        builder.Navigation(instance => instance.Costs).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
