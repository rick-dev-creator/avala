using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Avala.Sdk;

namespace Avala.Rendering.Sanitizing;

internal static partial class SvgSanitizer
{
    private static readonly HashSet<string> ForbiddenElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "foreignObject", "iframe", "object", "embed", "audio", "video", "handler", "listener",
    };

    private static readonly HashSet<string> Animations = new(StringComparer.OrdinalIgnoreCase) { "animate", "set", "animateMotion", "animateTransform" };

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        MaxCharactersInDocument = 4 * 1024 * 1024,
    };

    public static Option<SanitizedSvg> Sanitize(string markup)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(markup), Settings);
            var document = XDocument.Load(reader);

            if (document.Root is not { } root || !root.Name.LocalName.Equals("svg", StringComparison.Ordinal))
            {
                return Option<SanitizedSvg>.None;
            }

            var blocked = Clean(root);

            return new SanitizedSvg(root.ToString(SaveOptions.DisableFormatting), blocked);
        }
        catch (XmlException)
        {
            return Option<SanitizedSvg>.None;
        }
    }

    private static int Clean(XElement root)
    {
        var removed = root.Descendants()
            .Where(element => IsForbidden(element) && !element.Ancestors().Any(IsForbidden))
            .ToList();
        removed.ForEach(element => element.Remove());
        var instructions = root.DescendantNodes().OfType<XProcessingInstruction>().ToList();
        instructions.ForEach(instruction => instruction.Remove());

        var blocked = removed.Count + instructions.Count;

        foreach (var element in root.DescendantsAndSelf())
        {
            blocked += CleanAttributes(element);

            if (element.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase) && Unsafe(element.Value) is { } style)
            {
                element.Value = style.Text;
                blocked += style.Count;
            }
        }

        return blocked;
    }

    private static int CleanAttributes(XElement element)
    {
        var blocked = 0;

        foreach (var attribute in element.Attributes().ToList())
        {
            var name = attribute.Name.LocalName;

            if (name.StartsWith("on", StringComparison.OrdinalIgnoreCase) || (IsReference(name) && !IsLocal(attribute.Value)))
            {
                attribute.Remove();
                blocked++;
            }
            else if (Unsafe(attribute.Value) is { } value)
            {
                attribute.Value = value.Text;
                blocked += value.Count;
            }
        }

        return blocked;
    }

    private static bool IsForbidden(XElement element) =>
        ForbiddenElements.Contains(element.Name.LocalName) || IsScriptedAnimation(element);

    private static bool IsScriptedAnimation(XElement element) =>
        Animations.Contains(element.Name.LocalName)
        && element.Attribute("attributeName")?.Value is { } target
        && (IsReference(target) || target.StartsWith("on", StringComparison.OrdinalIgnoreCase));

    private static bool IsReference(string name) =>
        name.Equals("href", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(":href", StringComparison.OrdinalIgnoreCase)
        || name.Equals("src", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocal(string reference)
    {
        var value = reference.Trim();

        return value.StartsWith('#') || EmbeddedImages.Allow(value);
    }

    private static Rewritten? Unsafe(string text)
    {
        var count = 0;
        var rewritten = Import().Replace(text, _ =>
        {
            count++;

            return string.Empty;
        });
        rewritten = ExternalUrl().Replace(rewritten, _ =>
        {
            count++;

            return "none";
        });

        return count == 0 ? null : new Rewritten(rewritten, count);
    }

    [GeneratedRegex(@"url\(\s*(?!['""]?\s*#)[^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalUrl();

    [GeneratedRegex(@"@import[^;]*;?", RegexOptions.IgnoreCase)]
    private static partial Regex Import();

    private sealed record Rewritten(string Text, int Count);
}
