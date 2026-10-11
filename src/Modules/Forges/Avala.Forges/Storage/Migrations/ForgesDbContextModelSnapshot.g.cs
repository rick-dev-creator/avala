using System;
using Avala.Forges.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Forges.Storage.Migrations
{
    [DbContext(typeof(ForgesDbContext))]
    internal sealed partial class ForgesDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Forges.Storage.StoredCursor", b =>
                {
                    b.Property<string>("Trigger")
                        .HasColumnType("TEXT");

                    b.Property<string>("Cursor")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Trigger");

                    b.ToTable("EventCursors", (string)null);
                });

            modelBuilder.Entity("Avala.Forges.Storage.StoredEventEntry", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Entry")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("EventEntries", (string)null);
                });

            modelBuilder.Entity("Avala.Forges.Storage.StoredWakeUp", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<string>("WakeUp")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Job");

                    b.ToTable("WakeUps", (string)null);
                });

            modelBuilder.Entity("Avala.Forges.Storage.StoredWatch", b =>
                {
                    b.Property<Guid>("Job")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Watch")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Job");

                    b.ToTable("Watches", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
