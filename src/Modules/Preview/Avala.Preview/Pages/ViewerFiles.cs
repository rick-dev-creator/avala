using System.Collections.Frozen;
using Avala.Preview.Origins;

namespace Avala.Preview.Pages;

internal static class ViewerFiles
{
    private const string Folder = FileAddresses.ViewerFolder;

    private const string Prefix = "Viewer/";

    private static readonly FrozenDictionary<string, string> Names = typeof(ViewerFiles).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
        .ToFrozenDictionary(name => name.Replace('\\', '/'), name => name, StringComparer.Ordinal);

    public static bool Holds(string relative) => relative.StartsWith(Folder, StringComparison.Ordinal);

    public static bool Has(string relative) => Names.ContainsKey(NameOf(relative));

    public static Stream Open(string relative) =>
        Names.TryGetValue(NameOf(relative), out var name) ? typeof(ViewerFiles).Assembly.GetManifestResourceStream(name) ?? Stream.Null : Stream.Null;

    public static string Extension(string relative) => Path.GetExtension(NameOf(relative));

    private static string NameOf(string relative) => Prefix + (relative.Length == Folder.Length ? "index.html" : relative[Folder.Length..]);
}
