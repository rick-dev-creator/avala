using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Avala.Storage.Tests;

internal sealed class Note
{
    public int Key { get; init; }

    public string Text { get; init; } = string.Empty;
}

internal sealed class Tag
{
    public int Key { get; init; }

    public string Name { get; init; } = string.Empty;
}

internal sealed class LegacyNotesContext(string database) : DbContext
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSqlite($"Data Source={database};Pooling=False");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Note>().ToTable("Notes").HasKey(note => note.Key);
}

internal sealed class NotesContext(string database, params IInterceptor[] interceptors) : DbContext
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseSqlite($"Data Source={database};Pooling=False")
            .AddInterceptors(interceptors)
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Note>().ToTable("Notes").HasKey(note => note.Key);
        modelBuilder.Entity<Tag>().ToTable("Tags").HasKey(tag => tag.Key);
    }
}

[DbContext(typeof(NotesContext))]
[Migration("20260101000000_Initial")]
internal sealed class InitialNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            "Notes",
            table => new
            {
                Key = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Text = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Notes", note => note.Key));
}

[DbContext(typeof(NotesContext))]
[Migration("20260201000000_Tags")]
internal sealed class TagsAdded : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            "Tags",
            table => new
            {
                Key = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                Name = table.Column<string>(type: "TEXT", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_Tags", tag => tag.Key));
}

internal sealed class Before(string statement, Action interruption) : IDbCommandInterceptor
{
    public const string TakingTheLock = "INSERT OR IGNORE INTO \"__EFMigrationsLock\"";

    public const string CreatingTheHistory = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"";

    public const string ReleasingTheLock = "DELETE FROM \"__EFMigrationsLock\"";

    public const string ClearingTheLock = "DROP TABLE IF EXISTS \"__EFMigrationsLock\"";

    private bool interrupted;

    public static Before Stopping(string statement) => new(statement, () => throw new IOException($"Stopped before {statement}"));

    public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Interrupted(command, result));

    public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Interrupted(command, result));

    private T Interrupted<T>(DbCommand command, T result)
    {
        if (!interrupted && command.CommandText.Contains(statement, StringComparison.Ordinal))
        {
            interrupted = true;
            interruption();
        }

        return result;
    }
}
