using Avala.Sdk;

namespace Avala.Preview.Navigation;

internal static class RealFiles
{
    public static Option<string> Of(Uri address) =>
        address.IsAbsoluteUri && address.IsFile && address.LocalPath.Length > 0
            ? address.LocalPath.Canonical()
            : Option<string>.None;

    public static string Folder(string folder) => folder.Length == 0 ? folder : folder.Canonical();
}
