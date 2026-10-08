using Avala.Fixtures.Compliant.Domain;
using Microsoft.EntityFrameworkCore;

namespace Avala.Fixtures.Compliant.Infrastructure;

public sealed class OrdersDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
}
