using System;
using Avala.Budgets.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

#nullable disable

namespace Avala.Budgets.Storage.Migrations
{
    [DbContext(typeof(BudgetsDbContext))]
    internal sealed partial class BudgetsDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.12");

            modelBuilder.Entity("Avala.Budgets.Storage.StoredCarve", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<Guid>("Child")
                        .HasColumnType("TEXT");

                    b.Property<string>("Costs")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Parent")
                        .HasColumnType("TEXT");

                    b.Property<double>("Share")
                        .HasColumnType("REAL");

                    b.Property<long>("Tokens")
                        .HasColumnType("INTEGER");

                    b.HasKey("Key");

                    b.ToTable("Carves", (string)null);
                });

            modelBuilder.Entity("Avala.Budgets.Storage.StoredIntervention", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<long>("At")
                        .HasColumnType("INTEGER");

                    b.Property<decimal>("Cap")
                        .HasColumnType("TEXT");

                    b.Property<string>("Error")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Halt")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Job")
                        .HasColumnType("TEXT");

                    b.Property<string>("Measure")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<decimal>("Measured")
                        .HasColumnType("TEXT");

                    b.Property<string>("Reason")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Session")
                        .HasColumnType("TEXT");

                    b.Property<string>("Subject")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.ToTable("Interventions", (string)null);
                });

            modelBuilder.Entity("Avala.Budgets.Storage.StoredSessionBudget", b =>
                {
                    b.Property<int>("Key")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("INTEGER");

                    b.Property<string>("Budget")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<string>("Connection")
                        .IsRequired()
                        .HasColumnType("TEXT");

                    b.Property<Guid>("Session")
                        .HasColumnType("TEXT");

                    b.HasKey("Key");

                    b.HasIndex("Session");

                    b.ToTable("SessionBudgets", (string)null);
                });
#pragma warning restore 612, 618
        }
    }
}
