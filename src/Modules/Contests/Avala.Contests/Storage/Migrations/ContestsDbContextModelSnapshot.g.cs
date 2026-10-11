using System;
using Avala.Contests.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Contests.Storage.Migrations
{
    [DbContext(typeof(ContestsDbContext))]
    internal sealed partial class ContestsDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Contests.Storage.StoredContest", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("Contest")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("Started")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Status")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.HasIndex("Started");

                    b.ToTable("Contests", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
