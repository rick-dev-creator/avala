namespace Avala.Sdk;

public static class DeveloperMode
{
    public const string Variable = "AVALA_DEVELOPER";

    public static bool IsOn => Environment.GetEnvironmentVariable(Variable) is { } value
        && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
}
