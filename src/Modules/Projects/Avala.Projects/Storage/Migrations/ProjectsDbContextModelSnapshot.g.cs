using System;
using Avala.Projects.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Projects.Storage.Migrations
{
    [DbContext(typeof(ProjectsDbContext))]
    internal sealed partial class ProjectsDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Projects.Storage.StoredPlacement", b =>
                {
                    b.Property<Guid>("Job")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Project")
                        .HasColumnType("TEXT");

                    b.HasKey("Job");

                    b.ToTable("Placements", (string)null);
                });

            modelBuilder.Entity("Avala.Projects.Storage.StoredProject", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Repository")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("Projects", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
