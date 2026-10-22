using System;
using Avala.Documentation.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Documentation.Storage.Migrations
{
    [DbContext(typeof(DocumentationDbContext))]
    [Migration("20261022115756_UnwrittenVersions")]
    internal sealed partial class UnwrittenVersions
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Documentation.Storage.StoredChoice", b =>
                {
                    b.Property<Guid>("Job")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<string>("LastPlan")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Plan")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Slug")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Title")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Job");

                    b.ToTable("Choices", (string)null);
                });

            modelBuilder.Entity("Avala.Documentation.Storage.StoredRequest", b =>
                {
                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<int>("Number")
                        .HasColumnType("INTEGER");

                    b.Property<DateTimeOffset>("At")
                        .HasColumnType("TEXT");

                    b.Property<int>("Attempt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Text")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Version")
                        .HasColumnType("INTEGER");

                    b.HasKey("Job", "Number");

                    b.ToTable("Requests", (string)null);
                });

            modelBuilder.Entity("Avala.Documentation.Storage.StoredUnwritten", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<DateTimeOffset>("At")
                        .HasColumnType("TEXT");

                    b.Property<int>("Attempt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Failure")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Hold")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("Unwritten", (string)null);
                });

            modelBuilder.Entity("Avala.Documentation.Storage.StoredVersion", b =>
                {
                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<int>("Number")
                        .HasColumnType("INTEGER");

                    b.Property<DateTimeOffset>("At")
                        .HasColumnType("TEXT");

                    b.Property<int>("Attempt")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Path")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Writer")
                        .HasColumnType("TEXT");

                    b.HasKey("Job", "Number");

                    b.ToTable("Versions", (string)null);
                });

            modelBuilder.Entity("Avala.Documentation.Storage.StoredWriter", b =>
                {
                    b.Property<Guid>("Writer")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<string>("Kind")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Number")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Plan")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Writer");

                    b.ToTable("Writers", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
