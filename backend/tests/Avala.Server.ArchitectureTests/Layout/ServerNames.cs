namespace Avala.Server.ArchitectureTests.Layout;

internal static class ServerNames
{
    public const string Host = "Avala.Server";

    public const string CrossCuttingConcerns = "Avala.Server.CrossCuttingConcerns";

    public static string Project(string module, Layer layer) => $"{Host}.{module}.{layer}";

    public static string Tests(string module) => $"{Host}.{module}.Tests";

    public static bool TryParse(string name, out string module, out Layer layer)
    {
        layer = default;

        if (name.Split('.') is not ["Avala", "Server", var found, var suffix]
            || !Enum.TryParse(suffix, ignoreCase: false, out layer)
            || !Enum.IsDefined(layer))
        {
            module = string.Empty;

            return false;
        }

        module = found;

        return true;
    }
}
