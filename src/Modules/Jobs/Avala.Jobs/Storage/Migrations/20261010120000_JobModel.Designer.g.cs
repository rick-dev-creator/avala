using System;
using Avala.Jobs.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Jobs.Storage.Migrations
{
    [DbContext(typeof(JobsDbContext))]
    [Migration("20261010120000_JobModel")]
    internal sealed partial class JobModel
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Jobs.Jobs.Job", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("TEXT");

                    b.Property<string>("Autonomy")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<int>("Budget")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Connection")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("Ended")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Instruction")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Model")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Parent")
                        .HasColumnType("TEXT");

                    b.Property<string>("Repository")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Resume")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Session")
                        .HasColumnType("TEXT");

                    b.Property<string>("State")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<long>("Submitted")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("Workspace")
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("Jobs", (string)null);
                });

            modelBuilder.Entity("Avala.Jobs.Storage.StoredChoice", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<string>("Choice")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Job");

                    b.ToTable("ConnectionChoices", (string)null);
                });

            modelBuilder.Entity("Avala.Jobs.Jobs.Job", b =>
                {
                    b.OwnsMany("Avala.Jobs.Jobs.Attempt", "Attempts", b1 =>
                        {
                            b1.Property<int>("Key")
                                .ValueGeneratedOnAdd()
                                .HasColumnType("INTEGER");

                            b1.Property<string>("Guidance")
                                .IsRequired()
                                .HasColumnType("TEXT");

                            b1.Property<Guid>("JobId")
                                .HasColumnType("TEXT");

                            b1.Property<int>("Number")
                                .HasColumnType("INTEGER");

                            b1.Property<string>("Origin")
                                .IsRequired()
                                .HasColumnType("TEXT");

                            b1.Property<string>("Outcome")
                                .IsRequired()
                                .HasColumnType("TEXT");

                            b1.Property<Guid>("Session")
                                .HasColumnType("TEXT");

                            b1.HasKey("Key");

                            b1.HasIndex("JobId");

                            b1.ToTable("JobAttempts", (string)null);

                            b1.WithOwner()
                                .HasForeignKey("JobId");
                        });

                    b.Navigation("Attempts");
                });
#pragma warning restore 612, 618
        }
    }
}
