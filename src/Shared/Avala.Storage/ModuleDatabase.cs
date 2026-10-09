using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Avala.Storage;

public static class ModuleDatabase
{
    private const string OwnTables = """
        SELECT COUNT(*) AS "Value" FROM "sqlite_master"
        WHERE "type" = 'table' AND "name" NOT LIKE 'sqlite\_%' ESCAPE '\' AND "name" NOT LIKE '\_\_EFMigrations%' ESCAPE '\'
        """;

    private const string OrphanedLock = """DROP TABLE IF EXISTS "__EFMigrationsLock" """;

    public static async Task MigrateAsync(DbContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await context.GetService<IRelationalDatabaseCreator>().ExistsAsync(CancellationToken.None))
        {
            await context.Database.ExecuteSqlRawAsync(OrphanedLock, CancellationToken.None);
        }

        if (await CreatedWithoutMigrationsAsync(context, CancellationToken.None))
        {
            await AdoptAsync(context, CancellationToken.None);
        }

        await context.Database.MigrateAsync(CancellationToken.None);
    }

    private static async Task<bool> CreatedWithoutMigrationsAsync(DbContext context, CancellationToken cancellationToken) =>
        await context.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken)
        && !await context.GetService<IHistoryRepository>().ExistsAsync(cancellationToken)
        && await HasOwnTablesAsync(context, cancellationToken);

    private static async Task<bool> HasOwnTablesAsync(DbContext context, CancellationToken cancellationToken) =>
        await context.Database.SqlQueryRaw<int>(OwnTables).SingleAsync(cancellationToken) > 0;

    private static async Task AdoptAsync(DbContext context, CancellationToken cancellationToken)
    {
        var history = context.GetService<IHistoryRepository>();
        var initial = context.Database.GetMigrations().First();

        await context.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), cancellationToken);
        await context.Database.ExecuteSqlRawAsync(history.GetInsertScript(new HistoryRow(initial, ProductInfo.GetVersion())), cancellationToken);
    }
}
