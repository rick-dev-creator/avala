using Ardalis.Result;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Violating.Application;

internal sealed record RemindOrder(Guid Id) : ICommand<Result>;

internal sealed class ReminderDbContext(DbContextOptions<ReminderDbContext> options) : DbContext(options)
{
    public DbSet<Avala.Server.Violating.Domain.Order> Reminded => Set<Avala.Server.Violating.Domain.Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("violating");
}
