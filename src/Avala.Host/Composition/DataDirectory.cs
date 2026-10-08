using Avala.Sdk;

namespace Avala.Host.Composition;

internal static class DataDirectory
{
    private const string EnvironmentVariable = "AVALA_DATA_PATH";

    public static AvalaPaths Resolve() =>
        new(Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Avala"));
}
