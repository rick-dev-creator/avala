using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Avala.Fixtures.Compliant.Infrastructure;

[DbContext(typeof(OrdersDbContext))]
[Migration("20260101000000_Initial")]
public sealed class InitialOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
    }
}

[DbContext(typeof(OrdersDbContext))]
public sealed class OrdersDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
    }
}
