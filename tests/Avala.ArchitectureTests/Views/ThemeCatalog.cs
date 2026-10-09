using System.Globalization;
using System.Xml.Linq;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Views;

internal sealed class ThemeCatalog(IReadOnlyDictionary<string, string> brushes, IReadOnlyDictionary<string, string> metrics)
{
    private static readonly string ThemeDirectory =
        Path.Combine(SolutionLayout.SourceDirectory, "Shared", "Avala.Components.UI", "Theme");

    public static async Task<ThemeCatalog> ReadAsync(CancellationToken cancellationToken)
    {
        var files = await XamlFile.ReadAllAsync(
            [Path.Combine(ThemeDirectory, "Tokens.axaml"), Path.Combine(ThemeDirectory, "Metrics.axaml")],
            cancellationToken);
        var resources = files.SelectMany(file => file.Root.Descendants())
            .Select(element => (Element: element, Key: element.Attribute(XName.Get("Key", XamlFile.XamlNamespace))?.Value))
            .Where(resource => resource.Key is not null)
            .ToList();
        var brushes = resources
            .Where(resource => resource.Element.Name.LocalName == "SolidColorBrush" && resource.Element.Attribute("Color") is not null)
            .GroupBy(resource => Normalize(resource.Element.Attribute("Color")!.Value))
            .ToDictionary(group => group.Key, group => group.First().Key!, StringComparer.Ordinal);
        var metrics = resources
            .Where(resource => resource.Element.Name.LocalName is "Double" or "CornerRadius")
            .GroupBy(resource => $"{resource.Element.Name.LocalName}:{resource.Element.Value.Trim()}")
            .ToDictionary(group => group.Key, group => group.First().Key!, StringComparer.Ordinal);

        return new ThemeCatalog(brushes, metrics);
    }

    public string Suggest(string property, string value) => property[(property.LastIndexOf('.') + 1)..] switch
    {
        "FontSize" => metrics.TryGetValue($"Double:{value}", out var size)
            ? $"use {{DynamicResource {size}}} from the theme instead of {value}"
            : $"use a size of the type scale, such as {{DynamicResource FontSizeBody}}, or add {value} to the scale in Metrics.axaml",
        "CornerRadius" => metrics.TryGetValue($"CornerRadius:{value}", out var radius)
            ? $"use {{DynamicResource {radius}}} from the theme instead of {value}"
            : "use a radius of the theme, such as {DynamicResource RadiusCard}",
        "FontWeight" => $"use a typography class of the theme, such as Classes=\"strong\" or Classes=\"heading\", instead of {value}",
        "FontFamily" => "use {DynamicResource InterfaceFont} or {DynamicResource MonoFont} from the theme",
        "BoxShadow" => "use a shadow of the theme, such as {DynamicResource ShadowFloat}",
        _ => brushes.TryGetValue(Normalize(value), out var brush)
            ? $"use {{DynamicResource {brush}}} from the theme instead of {value}"
            : $"use a brush of the theme, such as {{DynamicResource TextPrimaryBrush}}, or add {value} to Tokens.axaml",
    };

    private static string Normalize(string color)
    {
        var hex = color.Trim().TrimStart('#').ToUpper(CultureInfo.InvariantCulture);

        return hex.Length == 6 ? $"#FF{hex}" : $"#{hex}";
    }
}
