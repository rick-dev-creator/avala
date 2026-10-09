using System;
using Avala.Observability.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Observability.Storage.Migrations
{
    [DbContext(typeof(ObservabilityDbContext))]
    [Migration("20261009184818_SessionOpened")]
    internal sealed partial class SessionOpened
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Observability.Storage.StoredFact", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<decimal>("Amount")
                        .HasColumnType("TEXT");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<long>("CacheRead")
                        .HasColumnType("INTEGER");

                    b.Property<long>("CacheWrite")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Currency")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("Duration")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Input")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Outcome")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("Output")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Reasoning")
                        .HasColumnType("INTEGER");

                    b.Property<long>("ResetsAt")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("Session")
                        .HasColumnType("TEXT");

                    b.Property<double>("UsedFraction")
                        .HasColumnType("REAL");

                    b.Property<string>("Window")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("At");

                    b.ToTable("UsageFacts", (string)null);
                });

            modelBuilder.Entity("Avala.Observability.Storage.StoredSession", b =>
                {
                    b.Property<Guid>("Session")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("AccountId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("AccountLabel")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Connection")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<long>("Opened")
                        .HasColumnType("INTEGER");

                    b.Property<string>("ProviderId")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("ProviderName")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Session");

                    b.ToTable("UsageSessions", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
