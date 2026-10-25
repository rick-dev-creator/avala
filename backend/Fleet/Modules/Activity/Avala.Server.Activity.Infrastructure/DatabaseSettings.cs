namespace Avala.Server.Activity.Infrastructure;

internal sealed class DatabaseSettings
{
    public const string Section = "Database";

    public bool ApplyMigrations { get; init; } = true;
}
