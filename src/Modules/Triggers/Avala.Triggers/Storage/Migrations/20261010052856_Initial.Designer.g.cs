using System;
using Avala.Triggers.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Triggers.Storage.Migrations
{
    [DbContext(typeof(TriggersDbContext))]
    [Migration("20261010052856_Initial")]
    internal sealed partial class Initial
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Triggers.Storage.StoredDelivery", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Fact")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("Deliveries", (string)null);
                });

            modelBuilder.Entity("Avala.Triggers.Storage.StoredRun", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Fact")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Run")
                        .HasColumnType("TEXT");

                    b.Property<string>("Trigger")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Run")
                        .IsUnique();

                    b.ToTable("Runs", (string)null);
                });

            modelBuilder.Entity("Avala.Triggers.Storage.StoredSchedule", b =>
                {
                    b.Property<string>("Trigger")
                        .HasColumnType("TEXT");

                    b.Property<long>("Anchor")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Enabled")
                        .HasColumnType("INTEGER");

                    b.Property<long>("LastFired")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Since")
                        .HasColumnType("INTEGER");

                    b.HasKey("Trigger");

                    b.ToTable("Schedules", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
