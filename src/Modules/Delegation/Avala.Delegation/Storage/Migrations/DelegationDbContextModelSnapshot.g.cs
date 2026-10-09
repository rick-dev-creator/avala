using System;
using Avala.Delegation.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Delegation.Storage.Migrations
{
    [DbContext(typeof(DelegationDbContext))]
    internal sealed partial class DelegationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Delegation.Storage.StoredRecord", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("Child")
                        .HasColumnType("TEXT");

                    b.Property<string>("Item")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Parent")
                        .HasColumnType("TEXT");

                    b.Property<string>("Record")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Session")
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Child");

                    b.HasIndex("Parent");

                    b.HasIndex("Session", "Item");

                    b.ToTable("Records", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
