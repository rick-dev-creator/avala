using System;
using Avala.Fleet.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Fleet.Storage.Migrations
{
    [DbContext(typeof(FleetDbContext))]
    [Migration("20261025091952_Initial")]
    internal sealed partial class Initial
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Fleet.Storage.AnnouncedRow", b =>
                {
                    b.Property<string>("Scope")
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<bool>("Keyless")
                        .HasColumnType("INTEGER");

                    b.HasKey("Scope", "Job");

                    b.ToTable("Announced", (string)null);
                });

            modelBuilder.Entity("Avala.Fleet.Storage.OutboxRow", b =>
                {
                    b.Property<long>("Sequence")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<int>("Attempts")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Body")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("DeferredReason")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("DueAt")
                        .HasColumnType("INTEGER");

                    b.Property<long>("FirstDeferredAt")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("Id")
                        .HasColumnType("TEXT");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("OccurredAt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Scope")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Sequence");

                    b.HasIndex("Id")
                        .IsUnique();

                    b.HasIndex("Scope", "DueAt");

                    b.ToTable("Outbox", (string)null);
                });

            modelBuilder.Entity("Avala.Fleet.Storage.RejectionRow", b =>
                {
                    b.Property<long>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Reason")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Scope")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Scope");

                    b.ToTable("Rejections", (string)null);
                });

            modelBuilder.Entity("Avala.Fleet.Storage.ScopeRow", b =>
                {
                    b.Property<string>("Scope")
                        .HasColumnType("TEXT");

                    b.Property<int>("Dropped")
                        .HasColumnType("INTEGER");

                    b.Property<long>("LastDelivery")
                        .HasColumnType("INTEGER");

                    b.HasKey("Scope");

                    b.ToTable("Scopes", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
