using Avala.Server.Activity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal sealed class ReceivedEventConfiguration : IEntityTypeConfiguration<ReceivedEvent>
{
    public void Configure(EntityTypeBuilder<ReceivedEvent> builder)
    {
        builder.ToTable("received_events");
        builder.HasKey(received => new { received.InstanceId, received.EventId });
        builder.Property(received => received.InstanceId).HasColumnName("instance_id");
        builder.Property(received => received.EventId).HasColumnName("event_id");
        builder.Property(received => received.ReceivedAt).HasColumnName("received_at");
    }
}
