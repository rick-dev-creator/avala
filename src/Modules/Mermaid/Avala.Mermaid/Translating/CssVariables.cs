using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avala.Sdk;

namespace Avala.Mermaid.Translating;

internal static partial class CssVariables
{
    private const int Depth = 16;
    private const double RootFontSize = 16;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        MaxCharactersInDocument = 4 * 1024 * 1024,
    };

    public static Option<string> Flatten(string svg)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), Settings);
            var document = XDocument.Load(reader);

            return document.Root is { } root ? Flattened(root) : Option<string>.None;
        }
        catch (XmlException)
        {
            return Option<string>.None;
        }
    }

    public static string Resolve(string value, IReadOnlyDictionary<string, string> variables) =>
        Units(CssColors.Mix(Substitute(value, variables, Depth)));

    private static string Flattened(XElement root)
    {
        var styles = root.Descendants().Where(element => element.Name.LocalName == "style").ToList();
        var variables = Declarations(string.Concat(styles.Select(style => style.Value)));

        foreach (var (name, value) in Declarations(root.Attribute("style")?.Value ?? string.Empty))
        {
            variables[name] = value;
        }

        foreach (var style in styles)
        {
            style.Value = Rules(style.Value, variables);
        }

        root.Attribute("style")?.Remove();

        foreach (var attribute in root.DescendantsAndSelf().SelectMany(element => element.Attributes()).Where(attribute => Dynamic(attribute.Value)))
        {
            attribute.Value = Resolve(attribute.Value, variables);
        }

        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static Dictionary<string, string> Declarations(string css) =>
        CustomProperty().Matches(css)
            .GroupBy(match => match.Groups["name"].Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Groups["value"].Value.Trim(), StringComparer.Ordinal);

    private static string Rules(string css, IReadOnlyDictionary<string, string> variables)
    {
        var withoutVariables = CustomProperty().Replace(css, string.Empty);
        var withoutFilters = Filter().Replace(withoutVariables, string.Empty);
        var withoutEmptyRules = EmptyRule().Replace(withoutFilters, string.Empty);

        return Dynamic(withoutEmptyRules) ? Resolve(withoutEmptyRules, variables) : withoutEmptyRules;
    }

    private static bool Dynamic(string value) =>
        value.Contains("var(", StringComparison.Ordinal) || value.Contains("color-mix(", StringComparison.Ordinal) || value.Contains("rem", StringComparison.Ordinal);

    private static string Substitute(string value, IReadOnlyDictionary<string, string> variables, int depth)
    {
        var result = value;
        var from = 0;

        while (depth > 0 && result.IndexOf("var(", from, StringComparison.Ordinal) is var start and >= 0)
        {
            var end = Closing(result, start + 3);

            if (end < 0)
            {
                return result;
            }

            var inside = result[(start + 4)..end];
            var comma = TopLevelComma(inside);
            var name = (comma < 0 ? inside : inside[..comma]).Trim();
            var fallback = comma < 0 ? string.Empty : inside[(comma + 1)..].Trim();
            var resolved = Substitute(variables.TryGetValue(name, out var declared) ? declared : fallback, variables, depth - 1);
            result = new StringBuilder(result).Remove(start, end - start + 1).Insert(start, resolved).ToString();
            from = start + resolved.Length;
        }

        return result;
    }

    private static int Closing(string value, int open)
    {
        var level = 0;

        for (var index = open; index < value.Length; index++)
        {
            level += value[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };

            if (level == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int TopLevelComma(string value)
    {
        var level = 0;

        for (var index = 0; index < value.Length; index++)
        {
            level += value[index] switch
            {
                '(' => 1,
                ')' => -1,
                _ => 0,
            };

            if (level == 0 && value[index] == ',')
            {
                return index;
            }
        }

        return -1;
    }

    private static string Units(string value) =>
        Rem().Replace(value, match => (double.Parse(match.Groups["size"].Value, NumberStyles.Float, CultureInfo.InvariantCulture) * RootFontSize).ToString("0.###", CultureInfo.InvariantCulture));

    [GeneratedRegex(@"(?<name>--[A-Za-z0-9_-]+)\s*:\s*(?<value>[^;}]*)(;|(?=\}))")]
    private static partial Regex CustomProperty();

    [GeneratedRegex(@"filter\s*:[^;}]*;?")]
    private static partial Regex Filter();

    [GeneratedRegex(@"[^{}]+\{\s*\}")]
    private static partial Regex EmptyRule();

    [GeneratedRegex(@"(?<![A-Za-z0-9.])(?<size>[0-9]*\.?[0-9]+)rem\b")]
    private static partial Regex Rem();
}
