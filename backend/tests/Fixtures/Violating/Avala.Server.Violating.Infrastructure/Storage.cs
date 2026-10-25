using Ardalis.Result;
using Avala.Server.Violating.Contracts;
using Avala.Server.Violating.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.Violating.Infrastructure;

internal sealed class OrderRow
{
    public Guid Id { get; set; }
}

internal sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderRow> Rows => Set<OrderRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("sales");
}

internal sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<Order> Audited => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("violating");
}

internal interface IOrderStore
{
    int Count();
}

internal sealed class OrderRepository(OrdersDbContext database) : IOrderStore
{
    public int Count() => database.Orders.Count();
}

internal sealed class CountOrdersHandler(IOrderStore store) : IQueryHandler<CountOrders, int>
{
    public ValueTask<int> Handle(CountOrders query, CancellationToken cancellationToken) => ValueTask.FromResult(store.Count());
}

internal sealed class ArchiveOrderHandler(OrdersDbContext database, TimeProvider clock) : ICommandHandler<ArchiveOrder, Result<ArchivedOrder>>
{
    public async ValueTask<Result<ArchivedOrder>> Handle(ArchiveOrder command, CancellationToken cancellationToken)
    {
        _ = clock.GetUtcNow();
        var found = await database.Orders.AnyAsync(order => order.Id == command.Id, cancellationToken);

        return found ? new ArchivedOrder(command.Id) : Result<ArchivedOrder>.NotFound();
    }
}
