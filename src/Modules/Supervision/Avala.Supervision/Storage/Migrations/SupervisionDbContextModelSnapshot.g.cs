using System;
using Avala.Supervision.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Supervision.Storage.Migrations
{
    [DbContext(typeof(SupervisionDbContext))]
    internal sealed partial class SupervisionDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Supervision.Storage.StoredIntervention", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Halt")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<string>("Reason")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Session")
                        .HasColumnType("TEXT");

                    b.Property<long>("Silent")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Window")
                        .HasColumnType("INTEGER");

                    b.HasKey("Key");

                    b.ToTable("Interventions", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
