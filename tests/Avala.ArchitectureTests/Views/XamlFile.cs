using System.Xml;
using System.Xml.Linq;

namespace Avala.ArchitectureTests.Views;

internal sealed record XamlFile(string Path, XDocument Document, int LineCount)
{
    public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    public const string DesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";

    private static readonly string[] ThemeRoots = ["ResourceDictionary", "Styles", "Application"];

    public XElement Root => Document.Root!;

    public bool IsView => !ThemeRoots.Contains(Root.Name.LocalName);

    public static async Task<IReadOnlyList<XamlFile>> ReadAllAsync(IEnumerable<string> paths, CancellationToken cancellationToken) =>
        await Task.WhenAll(paths.Select(async path =>
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);

            return new XamlFile(path, XDocument.Parse(text, LoadOptions.SetLineInfo), text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0));
        }));

    public static int LineOf(XObject node) => node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
}
