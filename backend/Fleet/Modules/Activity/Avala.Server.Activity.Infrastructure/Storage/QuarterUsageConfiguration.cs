using Avala.Server.Activity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal sealed class QuarterUsageConfiguration : IEntityTypeConfiguration<QuarterUsage>
{
    private const string InstanceColumn = "instance_id";

    private const string JobColumn = "job_id";

    private const string QuarterColumn = "quarter";

    private const string ModelColumn = "model";

    public void Configure(EntityTypeBuilder<QuarterUsage> builder)
    {
        builder.ToTable("quarter_usage");
        builder.HasKey(usage => new { usage.InstanceId, usage.JobId, usage.Quarter, usage.Model });
        builder.Property(usage => usage.InstanceId).HasColumnName(InstanceColumn);
        builder.Property(usage => usage.JobId).HasColumnName(JobColumn);
        builder.Property(usage => usage.Quarter).HasColumnName(QuarterColumn);
        builder.Property(usage => usage.Model).HasColumnName(ModelColumn).HasMaxLength(ModelChoice.ModelLength);
        builder.HasIndex(usage => usage.Quarter);
        builder.OwnsOne(usage => usage.Tokens, Columns.MapTokens);
        builder.OwnsMany(usage => usage.Costs, costs =>
        {
            costs.Property<Guid>(nameof(QuarterUsage.InstanceId)).HasColumnName(InstanceColumn);
            costs.Property<Guid>(nameof(QuarterUsage.JobId)).HasColumnName(JobColumn);
            costs.Property<DateTimeOffset>(nameof(QuarterUsage.Quarter)).HasColumnName(QuarterColumn);
            costs.Property<string>(nameof(QuarterUsage.Model)).HasColumnName(ModelColumn).HasMaxLength(ModelChoice.ModelLength);
            Columns.MapCosts(costs, "quarter_costs", nameof(QuarterUsage.InstanceId), nameof(QuarterUsage.JobId), nameof(QuarterUsage.Quarter), nameof(QuarterUsage.Model));
        });
        builder.Navigation(usage => usage.Costs).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
