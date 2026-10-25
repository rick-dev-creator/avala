using System;
using Avala.Server.Instances.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Avala.Server.Instances.Infrastructure.Storage.Migrations
{
    [DbContext(typeof(InstancesDbContext))]
    [Migration("20261025045833_Initial")]
    internal sealed partial class Initial
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("instances")
                .HasAnnotation("ProductVersion", "10.0.12")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("Avala.Server.Instances.Domain.Instance", b =>
                {
                    b.Property<Guid>("Id")
                        .HasColumnType("uuid")
                        .HasColumnName("id");

                    b.Property<string>("KeyHash")
                        .IsRequired()
                        .HasMaxLength(64)
                        .HasColumnType("character(64)")
                        .HasColumnName("key_hash")
                        .IsFixedLength();

                    b.Property<string>("Name")
                        .IsRequired()
                        .HasMaxLength(100)
                        .HasColumnType("character varying(100)")
                        .HasColumnName("name");

                    b.Property<DateTimeOffset>("RegisteredAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("registered_at");

                    b.Property<DateTimeOffset?>("RevokedAt")
                        .HasColumnType("timestamp with time zone")
                        .HasColumnName("revoked_at");

                    b.HasKey("Id");

                    b.HasIndex("KeyHash")
                        .IsUnique();

                    b.ToTable("instances", "instances");
                });
#pragma warning restore 612, 618
        }
    }
}
