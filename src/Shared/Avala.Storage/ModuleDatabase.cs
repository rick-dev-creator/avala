using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avala.Storage;

public static class ModuleDatabase
{
    public static async Task MigrateAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (await CreatedWithoutMigrationsAsync(context, cancellationToken))
        {
            await AdoptAsync(context, cancellationToken);
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    private static async Task<bool> CreatedWithoutMigrationsAsync(DbContext context, CancellationToken cancellationToken)
    {
        var creator = context.GetService<IRelationalDatabaseCreator>();

        return await creator.ExistsAsync(cancellationToken)
            && await creator.HasTablesAsync(cancellationToken)
            && !await context.GetService<IHistoryRepository>().ExistsAsync(cancellationToken);
    }

    private static async Task AdoptAsync(DbContext context, CancellationToken cancellationToken)
    {
        var history = context.GetService<IHistoryRepository>();
        var initial = context.Database.GetMigrations().First();

        await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(initial, ProductInfo.GetVersion())), cancellationToken);
    }
}
