namespace Avala.Host.Composition;

internal static class PluginDirectory
{
    private const string EnvironmentVariable = "AVALA_PLUGINS_PATH";

    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "plugins");

        return Directory.Exists(bundled)
            ? bundled
            : FindDevelopmentArtifacts(new DirectoryInfo(AppContext.BaseDirectory)) ?? bundled;
    }

    private static string? FindDevelopmentArtifacts(DirectoryInfo? directory)
    {
        if (directory is null)
        {
            return null;
        }

        var candidate = Path.Combine(directory.FullName, "artifacts", "plugins");

        return Directory.Exists(candidate) ? candidate : FindDevelopmentArtifacts(directory.Parent);
    }
}
