namespace Avala.Server.Instances.Infrastructure;

internal sealed class DatabaseSettings
{
    public const string Section = "Database";

    public bool ApplyMigrations { get; init; } = true;
}
