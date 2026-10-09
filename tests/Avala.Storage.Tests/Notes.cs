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

internal sealed class NotesContext(string database) : DbContext
{
    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder
            .UseSqlite($"Data Source={database};Pooling=False")
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
