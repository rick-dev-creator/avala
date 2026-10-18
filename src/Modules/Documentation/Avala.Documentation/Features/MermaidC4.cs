using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Features;

internal static partial class MermaidC4
{
    private static readonly Dictionary<string, C4Level> Levels = new(StringComparer.Ordinal)
    {
        ["C4Context"] = C4Level.Context,
        ["C4Container"] = C4Level.Container,
        ["C4Component"] = C4Level.Component,
        ["C4Dynamic"] = C4Level.Dynamic,
    };

    private static readonly Dictionary<string, C4Shape> Shapes = new(StringComparer.Ordinal)
    {
        ["Person"] = C4Shape.Person,
        ["System"] = C4Shape.System,
        ["Container"] = C4Shape.Container,
        ["Component"] = C4Shape.Component,
        ["SystemDb"] = C4Shape.Database,
        ["ContainerDb"] = C4Shape.Database,
        ["ComponentDb"] = C4Shape.Database,
        ["SystemQueue"] = C4Shape.Queue,
        ["ContainerQueue"] = C4Shape.Queue,
        ["ComponentQueue"] = C4Shape.Queue,
    };

    public static Option<C4Diagram> Read(string source)
    {
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("%%", StringComparison.Ordinal))
            .ToList();

        if (lines.Count == 0 || !Levels.TryGetValue(lines[0], out var level))
        {
            return Option<C4Diagram>.None;
        }

        var reading = new Reading(level);

        foreach (var line in lines.Skip(1))
        {
            reading.Take(line);
        }

        return reading.Diagram;
    }

    private sealed class Reading(C4Level level)
    {
        private readonly List<C4Element> elements = [];
        private readonly List<C4Boundary> boundaries = [];
        private readonly List<C4Relation> relations = [];
        private readonly Stack<string> open = new();
        private string title = string.Empty;

        public C4Diagram Diagram => new(level, title, elements, boundaries, relations);

        public void Take(string line)
        {
            if (line == "}")
            {
                open.TryPop(out _);
            }
            else if (line.StartsWith("title ", StringComparison.Ordinal))
            {
                title = line[6..].Trim();
            }
            else if (Call().Match(line) is { Success: true } call)
            {
                Called(call.Groups[1].Value, Arguments(call.Groups[2].Value), line.EndsWith('{'));
            }
        }

        private void Called(string name, List<string> arguments, bool opens)
        {
            var inside = open.Count > 0 ? Option<string>.Some(open.Peek()) : Option<string>.None;

            if (name.EndsWith("Boundary", StringComparison.Ordinal) && arguments.Count >= 1)
            {
                Keep(boundaries, new C4Boundary(arguments[0], At(arguments, 1, arguments[0])) { Parent = inside }, boundary => boundary.Alias);

                if (opens)
                {
                    open.Push(arguments[0]);
                }

                return;
            }

            Relation(name, arguments).Match(
                found => { relations.Add(found); return true; },
                () => Element(name, arguments, inside).Match(found => { Keep(elements, found, element => element.Alias); return true; }, () => false));
        }

        private static void Keep<T>(List<T> declared, T declaration, Func<T, string> alias)
        {
            var earlier = declared.FindIndex(found => alias(found) == alias(declaration));

            if (earlier >= 0)
            {
                declared[earlier] = declaration;
            }
            else
            {
                declared.Add(declaration);
            }
        }

        private Option<C4Relation> Relation(string name, List<string> arguments)
        {
            if (name == "RelIndex" && arguments.Count >= 4 && int.TryParse(arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                return new C4Relation(arguments[1], arguments[2], arguments[3]) { Technology = At(arguments, 4, string.Empty), Index = index };
            }

            if ((!name.StartsWith("Rel", StringComparison.Ordinal) && !name.StartsWith("BiRel", StringComparison.Ordinal)) || arguments.Count < 3)
            {
                return Option<C4Relation>.None;
            }

            var back = name == "Rel_Back";
            var order = level == C4Level.Dynamic ? Option<int>.Some(relations.Count + 1) : Option<int>.None;

            return new C4Relation(back ? arguments[1] : arguments[0], back ? arguments[0] : arguments[1], arguments[2]) { Technology = At(arguments, 3, string.Empty), Index = order };
        }

        private static Option<C4Element> Element(string name, List<string> arguments, Option<string> boundary)
        {
            var external = name.EndsWith("_Ext", StringComparison.Ordinal);
            var kind = external ? name[..^4] : name;

            if (arguments.Count < 2 || !Shapes.TryGetValue(kind, out var shape))
            {
                return Option<C4Element>.None;
            }

            var technical = kind.StartsWith("Container", StringComparison.Ordinal) || kind.StartsWith("Component", StringComparison.Ordinal);

            return new C4Element(arguments[0], arguments[1], shape, external)
            {
                Technology = technical ? At(arguments, 2, string.Empty) : string.Empty,
                Description = At(arguments, technical ? 3 : 2, string.Empty),
                Boundary = boundary,
            };
        }
    }

    private static string At(List<string> arguments, int index, string fallback) => index < arguments.Count ? arguments[index] : fallback;

    private static List<string> Arguments(string text)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        foreach (var letter in text)
        {
            if (letter == '"')
            {
                quoted = !quoted;
            }
            else if (letter == ',' && !quoted)
            {
                arguments.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(letter);
            }
        }

        arguments.Add(current.ToString().Trim());

        return [.. arguments.Where(argument => !argument.StartsWith('$'))];
    }

    [GeneratedRegex(@"^([A-Za-z_]+)\s*\((.*)\)\s*\{?$")]
    private static partial Regex Call();
}
