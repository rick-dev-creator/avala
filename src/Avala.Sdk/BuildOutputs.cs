namespace Avala.Sdk;

public static class BuildOutputs
{
    private static readonly char[] Separators = ['/', '\\'];

    private static readonly HashSet<string> Names = new(StringComparer.Ordinal)
    {
        "node_modules", "bin", "obj", "target", "dist", "build", ".venv", "venv", "__pycache__", ".gradle", ".next", ".nuxt", ".turbo", ".cache",
    };

    public static bool Holds(string relativePath) => relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Any(Names.Contains);
}
