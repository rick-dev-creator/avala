using Avala.Testing;
using Microsoft.EntityFrameworkCore;

namespace Avala.Storage.Tests;

public sealed class ModuleDatabaseTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AMissingDatabaseIsCreatedByEveryMigrationAsync()
    {
        await using var folder = new TemporaryFolder();
        await using var context = new NotesContext(Path.Combine(folder.Path, "notes.db"));

        await ModuleDatabase.MigrateAsync(context, Cancellation);

        Assert.Equal(["20260101000000_Initial", "20260201000000_Tags"], await context.Database.GetAppliedMigrationsAsync(Cancellation));
        Assert.Empty(await context.Tags.ToListAsync(Cancellation));
    }

    [Fact]
    public async Task ADatabaseCreatedWithoutMigrationsIsAdoptedAsItsInitialMigrationAndUpgradedKeepingItsRowsAsync()
    {
        await using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "notes.db");
        await using (var legacy = new LegacyNotesContext(path))
        {
            await legacy.Database.EnsureCreatedAsync(Cancellation);
            await legacy.Notes.AddAsync(new Note { Text = "kept" }, Cancellation);
            await legacy.SaveChangesAsync(Cancellation);
        }

        await using var context = new NotesContext(path);
        await ModuleDatabase.MigrateAsync(context, Cancellation);

        Assert.Equal(["20260101000000_Initial", "20260201000000_Tags"], await context.Database.GetAppliedMigrationsAsync(Cancellation));
        Assert.Equal(["kept"], await context.Notes.Select(note => note.Text).ToListAsync(Cancellation));
        await context.Tags.AddAsync(new Tag { Name = "new" }, Cancellation);
        Assert.Equal(1, await context.SaveChangesAsync(Cancellation));
    }

    [Fact]
    public async Task ADatabaseHoldingOnlyTheTableOfAMigrationsLockIsMigratedInsteadOfAdoptedAsync()
    {
        await using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "notes.db");
        await using (var stopped = new NotesContext(path, Before.Stopping(Before.TakingTheLock)))
        {
            await Assert.ThrowsAsync<IOException>(() => ModuleDatabase.MigrateAsync(stopped, Cancellation));
        }

        await using var context = new NotesContext(path);
        await ModuleDatabase.MigrateAsync(context, Cancellation);

        Assert.Equal(["20260101000000_Initial", "20260201000000_Tags"], await context.Database.GetAppliedMigrationsAsync(Cancellation));
        Assert.Empty(await context.Notes.ToListAsync(Cancellation));
    }

    [Fact]
    public async Task AMigrationWhoseCallerStopsHalfwayCompletesAndLeavesTheDatabaseFreeForTheNextAsync()
    {
        await using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "notes.db");
        using var caller = new CancellationTokenSource();
        await using (var stopped = new NotesContext(path, new Before(Before.CreatingTheHistory, caller.Cancel)))
        {
            await ModuleDatabase.MigrateAsync(stopped, caller.Token);
        }

        await using var context = new NotesContext(path);
        await ModuleDatabase.MigrateAsync(context, Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        Assert.Equal(["20260101000000_Initial", "20260201000000_Tags"], await context.Database.GetAppliedMigrationsAsync(Cancellation));
        Assert.Empty(await context.Tags.ToListAsync(Cancellation));
    }

    [Fact]
    public async Task ALockLeftByAProcessKilledWhileMigratingIsClearedAndEveryMigrationAppliesAsync()
    {
        await using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "notes.db");
        await using (var killed = new NotesContext(path, Before.Stopping(Before.CreatingTheHistory), Before.Stopping(Before.ReleasingTheLock)))
        {
            await Assert.ThrowsAsync<IOException>(() => ModuleDatabase.MigrateAsync(killed, Cancellation));
            Assert.Equal(1, await killed.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"__EFMigrationsLock\"").SingleAsync(Cancellation));
        }

        await using var context = new NotesContext(path);
        await ModuleDatabase.MigrateAsync(context, Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        Assert.Equal(["20260101000000_Initial", "20260201000000_Tags"], await context.Database.GetAppliedMigrationsAsync(Cancellation));
    }

    [Fact]
    public async Task AnOperationAfterAFailedOpeningRunsOnTheMigratedDatabaseAsync()
    {
        await using var folder = new TemporaryFolder();
        var opened = 0;
        await using var owner = new DatabaseOwner<NotesContext>(
            Path.Combine(folder.Path, "data", "notes.db"),
            file => ++opened == 1 ? new NotesContext(file, Before.Stopping(Before.TakingTheLock)) : new NotesContext(file));

        await Assert.ThrowsAsync<IOException>(() => owner.OpenedAsync(Cancellation));

        Assert.Empty(await owner.RunAsync(async database => await database.Notes.ToListAsync(Cancellation), Cancellation));
    }

    [Fact]
    public async Task MigratingAnUpToDateDatabaseAgainWritesNothingAsync()
    {
        await using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "notes.db");
        await using (var first = new NotesContext(path))
        {
            await ModuleDatabase.MigrateAsync(first, Cancellation);
            await first.Notes.AddAsync(new Note { Text = "kept" }, Cancellation);
            await first.SaveChangesAsync(Cancellation);
        }

        await using var again = new NotesContext(path, Before.Stopping(Before.ClearingTheLock), Before.Stopping(Before.TakingTheLock));
        await ModuleDatabase.MigrateAsync(again, Cancellation);

        Assert.Equal(2, (await again.Database.GetAppliedMigrationsAsync(Cancellation)).Count());
        Assert.Equal(["kept"], await again.Notes.Select(note => note.Text).ToListAsync(Cancellation));
    }
}
