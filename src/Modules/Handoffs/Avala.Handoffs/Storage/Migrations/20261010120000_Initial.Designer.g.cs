using System;
using Avala.Handoffs.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Handoffs.Storage.Migrations
{
    [DbContext(typeof(HandoffsDbContext))]
    [Migration("20261010120000_Initial")]
    internal sealed partial class Initial
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Handoffs.Storage.StoredHandoff", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Handoff")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Job");

                    b.ToTable("Handoffs", (string)null);
                });

            modelBuilder.Entity("Avala.Handoffs.Storage.StoredWait", b =>
                {
                    b.Property<Guid>("Job")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Pending")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("Since")
                        .HasColumnType("INTEGER");

                    b.HasKey("Job");

                    b.ToTable("Waits", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
