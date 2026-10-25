using System.Collections.Frozen;
using System.Globalization;

namespace Avala.Preview.Origins;

internal static class FileAddresses
{
    public const string ViewerFolder = "__avala/viewer/";

    private static readonly FrozenSet<string> Models = new[] { ".glb", ".gltf", ".stl", ".obj" }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsModel(string path) => Models.Contains(Extension(path));

    public static Uri Viewed(Uri file, int version)
    {
        var relative = Uri.UnescapeDataString(file.AbsolutePath).TrimStart('/');

        return IsModel(relative)
            ? new Uri(file, string.Create(CultureInfo.InvariantCulture, $"/{ViewerFolder}index.html?model={Uri.EscapeDataString(relative)}&v={version}"))
            : new Uri(file, string.Create(CultureInfo.InvariantCulture, $"{file.AbsolutePath}?v={version}"));
    }

    public static string Extension(string path)
    {
        var name = path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
        var dot = name.LastIndexOf('.');

        return dot > 0 ? name[dot..].ToLowerInvariant() : string.Empty;
    }

    public static Uri Served(Uri root, string relative) =>
        new(root, string.Join('/', relative.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString)));
}
