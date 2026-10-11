using Microsoft.EntityFrameworkCore;

namespace Avala.Triggers.Storage;

internal sealed class TriggersDbContext(string database) : DbContext
{
    public DbSet<StoredSchedule> Schedules => Set<StoredSchedule>();

    public DbSet<StoredRun> Runs => Set<StoredRun>();

    public DbSet<StoredDelivery> Deliveries => Set<StoredDelivery>();

    public DbSet<StoredQueuedTask> Queued => Set<StoredQueuedTask>();

    public DbSet<StoredEventKey> EventKeys => Set<StoredEventKey>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredSchedule>(schedule =>
        {
            schedule.ToTable("Schedules");
            schedule.HasKey(row => row.Trigger);
        });
        modelBuilder.Entity<StoredRun>(run =>
        {
            run.ToTable("Runs");
            run.HasKey(row => row.Key);
            run.Property(row => row.Key).ValueGeneratedOnAdd();
            run.HasIndex(row => row.Run).IsUnique();
        });
        modelBuilder.Entity<StoredDelivery>(delivery =>
        {
            delivery.ToTable("Deliveries");
            delivery.HasKey(row => row.Key);
            delivery.Property(row => row.Key).ValueGeneratedOnAdd();
        });
        modelBuilder.Entity<StoredQueuedTask>(task =>
        {
            task.ToTable("QueuedTasks");
            task.HasKey(row => row.Key);
            task.Property(row => row.Key).ValueGeneratedOnAdd();
            task.HasIndex(row => row.Run).IsUnique();
        });
        modelBuilder.Entity<StoredEventKey>(key =>
        {
            key.ToTable("EventKeys");
            key.HasKey(row => row.Key);
            key.Property(row => row.Key).ValueGeneratedOnAdd();
            key.HasIndex(row => row.Value).IsUnique();
        });
    }
}
