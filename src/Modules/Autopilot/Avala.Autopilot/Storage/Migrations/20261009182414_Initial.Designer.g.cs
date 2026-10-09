using System;
using Avala.Autopilot.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Autopilot.Storage.Migrations
{
    [DbContext(typeof(AutopilotDbContext))]
    [Migration("20261009182414_Initial")]
    internal sealed partial class Initial
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Autopilot.Storage.StoredTask", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Instruction")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<string>("Repository")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Source")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("State")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("Taken")
                        .HasColumnType("INTEGER");

                    b.Property<string>("TaskKey")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Repository", "Source", "TaskKey")
                        .IsUnique();

                    b.ToTable("Tasks", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
