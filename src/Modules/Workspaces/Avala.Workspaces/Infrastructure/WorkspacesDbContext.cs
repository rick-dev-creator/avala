using Avala.Workspaces.Contracts;
using Avala.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avala.Workspaces.Infrastructure;

internal sealed class WorkspacesDbContext(string database) : DbContext
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Workspace>(workspace =>
        {
            workspace.ToTable("Workspaces");
            workspace.HasKey(entity => entity.Id);
            workspace.Property(entity => entity.Id).HasConversion(id => id.Value, value => new WorkspaceId(value));
            workspace.Property(entity => entity.Location).HasConversion(location => Stored.Write(location), json => Stored.Location(json));
            workspace.Property(entity => entity.Branch).HasConversion(branch => branch.Value, name => Stored.Branch(name));
            workspace.Property(entity => entity.State).HasConversion<string>();
            workspace.OwnsMany(entity => entity.Checkpoints, checkpoint =>
            {
                checkpoint.ToTable("WorkspaceCheckpoints");
                checkpoint.WithOwner().HasForeignKey("WorkspaceId");
                checkpoint.Property<int>("Key").ValueGeneratedOnAdd();
                checkpoint.HasKey("Key");
                checkpoint.Property(entity => entity.Commit).HasConversion(commit => commit.Value, sha => Stored.Commit(sha));
            });
        });
}
