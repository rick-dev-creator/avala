using System;
using Avala.Server.Activity.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avala.Server.Activity.Infrastructure.Storage.Migrations
{
    [DbContext(typeof(ActivityDbContext))]
    internal sealed partial class ActivityDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("activity")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Avala.Server.Activity.Domain.FleetInstance", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<DateTimeOffset>("LastSeenAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_seen_at");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasColumnName("name");

                    b.HasKey("Id");

                    b.ToTable("instances", "activity");
                });

            modelBuilder.Entity("Avala.Server.Activity.Domain.FleetJob", b =>
                {
                    b.Property<Guid>("InstanceId")
                        .HasColumnType("uuid")
                        .HasColumnName("instance_id");

                    b.Property<Guid>("JobId")
                        .HasColumnType("uuid")
                        .HasColumnName("job_id");

                    b.Property<int>("Attempts")
                        .HasColumnType("integer")
                        .HasColumnName("attempts");

                    b.Property<string>("Effort")
                        .HasMaxLength(20)
                        .HasColumnType("character varying(20)")
                        .HasColumnName("effort");

                    b.Property<DateTimeOffset>("FirstActivityAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("first_activity_at");

                    b.Property<string>("Hold")
                        .HasMaxLength(30)
                        .HasColumnType("character varying(30)")
                        .HasColumnName("hold");

                    b.Property<DateTimeOffset?>("HoldAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("hold_at");

                    b.Property<DateTimeOffset>("LastActivityAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("last_activity_at");

                    b.Property<string>("LastOutcome")
                        .HasMaxLength(30)
                        .HasColumnType("character varying(30)")
                        .HasColumnName("last_outcome");

                    b.Property<string>("Model")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("model");

                    b.Property<DateTimeOffset?>("ModelAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("model_at");

                    b.Property<DateTimeOffset?>("OutcomeAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("outcome_at");

                    b.Property<Guid?>("ParentId")
                        .HasColumnType("uuid")
                        .HasColumnName("parent_id");

                    b.Property<string>("Repository")
                        .HasMaxLength(300)
                        .HasColumnType("character varying(300)")
                        .HasColumnName("repository");

                    b.Property<string>("Status")
                        .HasMaxLength(30)
                        .HasColumnType("character varying(30)")
                        .HasColumnName("status");

                    b.Property<DateTimeOffset?>("StatusAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("status_at");

                    b.HasKey("InstanceId", "JobId");

                    b.HasIndex("Repository");

                    b.ToTable("jobs", "activity");
                });

            modelBuilder.Entity("Avala.Server.Activity.Domain.QuarterUsage", b =>
                {
                    b.Property<Guid>("InstanceId")
                        .HasColumnType("uuid")
                        .HasColumnName("instance_id");

                    b.Property<Guid>("JobId")
                        .HasColumnType("uuid")
                        .HasColumnName("job_id");

                    b.Property<DateTimeOffset>("Quarter")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("quarter");

                    b.Property<string>("Model")
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("model");

                    b.HasKey("InstanceId", "JobId", "Quarter", "Model");

                    b.HasIndex("Quarter");

                    b.ToTable("quarter_usage", "activity");
                });

            modelBuilder.Entity("Avala.Server.Activity.Domain.ReceivedEvent", b =>
                {
                    b.Property<Guid>("InstanceId")
                        .HasColumnType("uuid")
                        .HasColumnName("instance_id");

                    b.Property<Guid>("EventId")
                        .HasColumnType("uuid")
                        .HasColumnName("event_id");

                    b.Property<DateTimeOffset>("ReceivedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("received_at");

                    b.HasKey("InstanceId", "EventId");

                    b.ToTable("received_events", "activity");
                });

            modelBuilder.Entity("Avala.Server.Activity.Domain.FleetInstance", b =>
                {
                    b.OwnsOne("Avala.Server.Activity.Domain.TokenTotals", "Tokens", b1 =>
                        {
                            b1.Property<Guid>("FleetInstanceId")
                                .HasColumnType("uuid");

                            b1.Property<decimal>("CacheRead")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("cache_read_tokens");

                            b1.Property<decimal>("CacheWrite")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("cache_write_tokens");

                            b1.Property<decimal>("Input")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("input_tokens");

                            b1.Property<decimal>("Output")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("output_tokens");

                            b1.Property<decimal>("Reasoning")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("reasoning_tokens");

                            b1.HasKey("FleetInstanceId");

                            b1.ToTable("instances", "activity");

                            b1.WithOwner()
                                .HasForeignKey("FleetInstanceId");
                        });

                    b.OwnsMany("Avala.Server.Activity.Domain.CostTotal", "Costs", b1 =>
                        {
                            b1.Property<Guid>("instance_id")
                                .HasColumnType("uuid")
                                .HasColumnName("instance_id");

                            b1.Property<string>("Currency")
                                .HasMaxLength(3)
                                .HasColumnType("character(3)")
                                .HasColumnName("currency")
                                .IsFixedLength();

                            b1.Property<string>("Basis")
                                .HasMaxLength(30)
                                .HasColumnType("character varying(30)")
                                .HasColumnName("basis");

                            b1.Property<decimal>("Amount")
                                .HasPrecision(28, 6)
                                .HasColumnType("numeric(28,6)")
                                .HasColumnName("amount");

                            b1.HasKey("instance_id", "Currency", "Basis");

                            b1.ToTable("instance_costs", "activity");

                            b1.WithOwner()
                                .HasForeignKey("instance_id");
                        });

                    b.Navigation("Costs");

                    b.Navigation("Tokens")
                        .IsRequired();
                });

            modelBuilder.Entity("Avala.Server.Activity.Domain.FleetJob", b =>
                {
                    b.OwnsOne("Avala.Server.Activity.Domain.TokenTotals", "Tokens", b1 =>
                        {
                            b1.Property<Guid>("FleetJobInstanceId")
                                .HasColumnType("uuid");

                            b1.Property<Guid>("FleetJobJobId")
                                .HasColumnType("uuid");

                            b1.Property<decimal>("CacheRead")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("cache_read_tokens");

                            b1.Property<decimal>("CacheWrite")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("cache_write_tokens");

                            b1.Property<decimal>("Input")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("input_tokens");

                            b1.Property<decimal>("Output")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("output_tokens");

                            b1.Property<decimal>("Reasoning")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("reasoning_tokens");

                            b1.HasKey("FleetJobInstanceId", "FleetJobJobId");

                            b1.ToTable("jobs", "activity");

                            b1.WithOwner()
                                .HasForeignKey("FleetJobInstanceId", "FleetJobJobId");
                        });

                    b.OwnsMany("Avala.Server.Activity.Domain.CostTotal", "Costs", b1 =>
                        {
                            b1.Property<Guid>("InstanceId")
                                .HasColumnType("uuid")
                                .HasColumnName("instance_id");

                            b1.Property<Guid>("JobId")
                                .HasColumnType("uuid")
                                .HasColumnName("job_id");

                            b1.Property<string>("Currency")
                                .HasMaxLength(3)
                                .HasColumnType("character(3)")
                                .HasColumnName("currency")
                                .IsFixedLength();

                            b1.Property<string>("Basis")
                                .HasMaxLength(30)
                                .HasColumnType("character varying(30)")
                                .HasColumnName("basis");

                            b1.Property<decimal>("Amount")
                                .HasPrecision(28, 6)
                                .HasColumnType("numeric(28,6)")
                                .HasColumnName("amount");

                            b1.HasKey("InstanceId", "JobId", "Currency", "Basis");

                            b1.ToTable("job_costs", "activity");

                            b1.WithOwner()
                                .HasForeignKey("InstanceId", "JobId");
                        });

                    b.Navigation("Costs");

                    b.Navigation("Tokens")
                        .IsRequired();
                });

            modelBuilder.Entity("Avala.Server.Activity.Domain.QuarterUsage", b =>
                {
                    b.OwnsMany("Avala.Server.Activity.Domain.CostTotal", "Costs", b1 =>
                        {
                            b1.Property<Guid>("InstanceId")
                                .HasColumnType("uuid")
                                .HasColumnName("instance_id");

                            b1.Property<Guid>("JobId")
                                .HasColumnType("uuid")
                                .HasColumnName("job_id");

                            b1.Property<DateTimeOffset>("Quarter")
                                .HasColumnType("timestamp with time zone")
                                .HasColumnName("quarter");

                            b1.Property<string>("Model")
                                .HasMaxLength(100)
                                .HasColumnType("character varying(100)")
                                .HasColumnName("model");

                            b1.Property<string>("Currency")
                                .HasMaxLength(3)
                                .HasColumnType("character(3)")
                                .HasColumnName("currency")
                                .IsFixedLength();

                            b1.Property<string>("Basis")
                                .HasMaxLength(30)
                                .HasColumnType("character varying(30)")
                                .HasColumnName("basis");

                            b1.Property<decimal>("Amount")
                                .HasPrecision(28, 6)
                                .HasColumnType("numeric(28,6)")
                                .HasColumnName("amount");

                            b1.HasKey("InstanceId", "JobId", "Quarter", "Model", "Currency", "Basis");

                            b1.ToTable("quarter_costs", "activity");

                            b1.WithOwner()
                                .HasForeignKey("InstanceId", "JobId", "Quarter", "Model");
                        });

                    b.OwnsOne("Avala.Server.Activity.Domain.TokenTotals", "Tokens", b1 =>
                        {
                            b1.Property<Guid>("QuarterUsageInstanceId")
                                .HasColumnType("uuid");

                            b1.Property<Guid>("QuarterUsageJobId")
                                .HasColumnType("uuid");

                            b1.Property<DateTimeOffset>("QuarterUsageQuarter")
                                .HasColumnType("timestamp with time zone");

                            b1.Property<string>("QuarterUsageModel")
                                .HasColumnType("character varying(100)");

                            b1.Property<decimal>("CacheRead")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("cache_read_tokens");

                            b1.Property<decimal>("CacheWrite")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("cache_write_tokens");

                            b1.Property<decimal>("Input")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("input_tokens");

                            b1.Property<decimal>("Output")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("output_tokens");

                            b1.Property<decimal>("Reasoning")
                                .HasPrecision(30)
                                .HasColumnType("numeric(30,0)")
                                .HasColumnName("reasoning_tokens");

                            b1.HasKey("QuarterUsageInstanceId", "QuarterUsageJobId", "QuarterUsageQuarter", "QuarterUsageModel");

                            b1.ToTable("quarter_usage", "activity");

                            b1.WithOwner()
                                .HasForeignKey("QuarterUsageInstanceId", "QuarterUsageJobId", "QuarterUsageQuarter", "QuarterUsageModel");
                        });

                    b.Navigation("Costs");

                    b.Navigation("Tokens")
                        .IsRequired();
                });
#pragma warning restore 612, 618
        }
    }
}
