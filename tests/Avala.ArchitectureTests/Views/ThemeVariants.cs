using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Avala.ArchitectureTests.Views;

internal static partial class ThemeVariants
{
    public static readonly IReadOnlyList<string> Required = ["Dark", "Light"];

    private static readonly HashSet<string> VariantContainers = ["ResourceDictionary.ThemeDictionaries", "FluentTheme.Palettes"];

    public static IEnumerable<string> Findings(string path, XDocument document) =>
        IncompleteVariants(path, document.Root!).Concat(ColorsOutsideVariants(path, document.Root!));

    private static IEnumerable<string> IncompleteVariants(string path, XElement root) =>
        root.DescendantsAndSelf()
            .Where(element => VariantContainers.Contains(element.Name.LocalName))
            .SelectMany(container => Compare(path, container));

    private static IEnumerable<string> Compare(string path, XElement container)
    {
        var variants = container.Elements().ToDictionary(variant => KeyOf(variant) ?? string.Empty, KeysOf, StringComparer.Ordinal);
        var line = XamlFile.LineOf(container);

        foreach (var missing in Required.Where(variant => !variants.ContainsKey(variant)))
        {
            yield return $"{path}:{line} declares no {missing} variant; every theme dictionary has both Dark and Light";
        }

        if (variants.TryGetValue("Dark", out var dark) && variants.TryGetValue("Light", out var light))
        {
            foreach (var key in dark.Except(light).Order(StringComparer.Ordinal))
            {
                yield return $"{path}:{line} defines {key} for Dark only; add its Light value";
            }

            foreach (var key in light.Except(dark).Order(StringComparer.Ordinal))
            {
                yield return $"{path}:{line} defines {key} for Light only; add its Dark value";
            }
        }
    }

    private static IEnumerable<string> ColorsOutsideVariants(string path, XElement root) =>
        root.DescendantsAndSelf()
            .Where(element => !element.Ancestors().Any(ancestor => VariantContainers.Contains(ancestor.Name.LocalName)))
            .SelectMany(element => element.Attributes().Where(attribute => attribute.Name.LocalName != "Selector").Select(attribute => (Node: (XObject)attribute, Text: attribute.Value))
                .Concat(element.Nodes().OfType<XText>().Select(text => (Node: (XObject)text, Text: text.Value))))
            .Where(value => ColorLiteral().IsMatch(value.Text))
            .Select(value => $"{path}:{XamlFile.LineOf(value.Node)} holds the color {ColorLiteral().Match(value.Text).Value} outside a theme dictionary; move it into the Dark and Light dictionaries");

    private static HashSet<string> KeysOf(XElement variant) =>
        [.. variant.Descendants().Select(KeyOf).OfType<string>()];

    private static string? KeyOf(XElement element) =>
        element.Attribute(XName.Get("Key", XamlFile.XamlNamespace))?.Value;

    [GeneratedRegex("#[0-9A-Fa-f]{3,8}\\b")]
    private static partial Regex ColorLiteral();
}
