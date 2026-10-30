using Avala.Server.Activity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Avala.Server.Activity.Infrastructure.Storage;

internal sealed class FleetJobConfiguration : IEntityTypeConfiguration<FleetJob>
{
    private const string InstanceColumn = "instance_id";

    private const string JobColumn = "job_id";

    public void Configure(EntityTypeBuilder<FleetJob> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(job => new { job.InstanceId, job.JobId });
        builder.Property(job => job.InstanceId).HasColumnName(InstanceColumn);
        builder.Property(job => job.JobId).HasColumnName(JobColumn);
        builder.Property(job => job.Repository)
            .HasColumnName("repository")
            .HasMaxLength(RepositoryKey.MaximumLength)
            .HasConversion(key => key!.Value, value => new RepositoryKey(value));
        builder.HasIndex(job => job.Repository);
        builder.Property(job => job.ParentId).HasColumnName("parent_id");
        builder.Property(job => job.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(Columns.EnumLength);
        builder.Property(job => job.StatusAt).HasColumnName("status_at");
        builder.Property(job => job.Hold).HasColumnName("hold").HasConversion<string>().HasMaxLength(Columns.EnumLength);
        builder.Property(job => job.HoldAt).HasColumnName("hold_at");
        builder.Property(job => job.Model).HasColumnName("model").HasMaxLength(ModelChoice.ModelLength);
        builder.Property(job => job.Effort).HasColumnName("effort").HasMaxLength(ModelChoice.EffortLength);
        builder.Property(job => job.ModelAt).HasColumnName("model_at");
        builder.Property(job => job.Attempts).HasColumnName("attempts");
        builder.Property(job => job.LastOutcome).HasColumnName("last_outcome").HasConversion<string>().HasMaxLength(Columns.EnumLength);
        builder.Property(job => job.OutcomeAt).HasColumnName("outcome_at");
        builder.Property(job => job.FirstActivityAt).HasColumnName("first_activity_at");
        builder.Property(job => job.LastActivityAt).HasColumnName("last_activity_at");
        builder.Ignore(job => job.VisibleHold);
        builder.OwnsOne(job => job.Tokens, Columns.MapTokens);
        builder.OwnsMany(job => job.Costs, costs =>
        {
            costs.Property<Guid>(nameof(FleetJob.InstanceId)).HasColumnName(InstanceColumn);
            costs.Property<Guid>(nameof(FleetJob.JobId)).HasColumnName(JobColumn);
            Columns.MapCosts(costs, "job_costs", nameof(FleetJob.InstanceId), nameof(FleetJob.JobId));
        });
        builder.Navigation(job => job.Costs).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
