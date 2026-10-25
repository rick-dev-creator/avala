using Avala.Server.Instances.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avala.Server.Instances.Infrastructure.Storage;

internal sealed class InstanceConfiguration : IEntityTypeConfiguration<Instance>
{
    public void Configure(EntityTypeBuilder<Instance> builder)
    {
        builder.ToTable("instances");
        builder.HasKey(instance => instance.Id);
        builder.Property(instance => instance.Id).HasConversion(id => id.Value, value => new InstanceId(value));
        builder.Property(instance => instance.Name).HasMaxLength(Instance.NameLength).IsRequired();
        builder.Property(instance => instance.RegisteredAt);
    }
}
