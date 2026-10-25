using Ardalis.Result;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Violating.Application;

internal sealed record RemindOrder(Guid Id) : ICommand<Result>;

internal sealed class ReminderDbContext(DbContextOptions<ReminderDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("violating");
}
