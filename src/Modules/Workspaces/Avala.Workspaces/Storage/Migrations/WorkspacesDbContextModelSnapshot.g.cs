using System;
using Avala.Workspaces.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Workspaces.Storage.Migrations
{
    [DbContext(typeof(WorkspacesDbContext))]
    internal sealed partial class WorkspacesDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Workspaces.Workspaces.Workspace", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("TEXT");

                    b.Property<string>("Base")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("BaseBranch")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Branch")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Location")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Rules")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("State")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Id");

                    b.ToTable("Workspaces", (string)null);
                });

            modelBuilder.Entity("Avala.Workspaces.Workspaces.Workspace", b =>
                {
                    b.OwnsMany("Avala.Workspaces.Workspaces.Checkpoint", "Checkpoints", b1 =>
                        {
                            b1.Property<int>("Key")
                                .ValueGeneratedOnAdd()
                                .HasColumnType("INTEGER");

                            b1.Property<string>("Commit")
                                .IsRequired()
                                .HasColumnType("TEXT");

                            b1.Property<string>("Label")
                                .IsRequired()
                                .HasColumnType("TEXT");

                            b1.Property<int>("Number")
                                .HasColumnType("INTEGER");

                            b1.Property<Guid>("WorkspaceId")
                                .HasColumnType("TEXT");

                            b1.HasKey("Key");

                            b1.HasIndex("WorkspaceId");

                            b1.ToTable("WorkspaceCheckpoints", (string)null);

                            b1.WithOwner()
                                .HasForeignKey("WorkspaceId");
                        });

                    b.Navigation("Checkpoints");
                });
#pragma warning restore 612, 618
        }
    }
}
