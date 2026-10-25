using System;
using Avala.Resources.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Resources.Storage.Migrations
{
    [DbContext(typeof(ResourcesDbContext))]
    internal sealed partial class ResourcesDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Resources.Storage.StoredSeenWorktree", b =>
                {
                    b.Property<string>("Path")
                        .HasColumnType("TEXT");

                    b.Property<long>("First")
                        .HasColumnType("INTEGER");

                    b.HasKey("Path");

                    b.ToTable("SeenWorktrees", (string)null);
                });

            modelBuilder.Entity("Avala.Resources.Storage.StoredWorktreeDay", b =>
                {
                    b.Property<string>("Day")
                        .HasColumnType("TEXT");

                    b.Property<int>("Created")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Freed")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Peak")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Recovered")
                        .HasColumnType("INTEGER");

                    b.Property<int>("Worktrees")
                        .HasColumnType("INTEGER");

                    b.HasKey("Day");

                    b.ToTable("WorktreeDays", (string)null);
                });

            modelBuilder.Entity("Avala.Resources.Storage.StoredWorktreeEvent", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<long>("Bytes")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<long>("Lifetime")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Moment")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Path")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("At");

                    b.ToTable("WorktreeEvents", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
