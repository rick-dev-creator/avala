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
        builder.Property(instance => instance.Id).HasColumnName("id").HasConversion(id => id.Value, value => new InstanceId(value));
        builder.Property(instance => instance.Name).HasColumnName("name").HasMaxLength(Instance.NameLength).IsRequired();
        builder.Property(instance => instance.KeyHash).HasColumnName("key_hash").HasMaxLength(InstanceKey.HashLength).IsFixedLength().IsRequired();
        builder.HasIndex(instance => instance.KeyHash).IsUnique();
        builder.Property(instance => instance.RegisteredAt).HasColumnName("registered_at");
        builder.Property(instance => instance.RevokedAt).HasColumnName("revoked_at");
    }
}
