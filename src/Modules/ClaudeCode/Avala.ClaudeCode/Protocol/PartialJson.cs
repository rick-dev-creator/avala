using System.Globalization;
using System.Text;

namespace Avala.ClaudeCode.Protocol;

internal readonly record struct PartialText(string Value, bool Complete);

internal static class PartialJson
{
    public static IReadOnlyDictionary<string, PartialText> Strings(string json)
    {
        var found = new Dictionary<string, PartialText>(StringComparer.Ordinal);
        var at = Expect(json, Skip(json, 0), '{');

        while (at >= 0)
        {
            at = Member(json, Skip(json, at), found);
        }

        return found;
    }

    private static int Member(string json, int at, Dictionary<string, PartialText> found)
    {
        if (!Starts(json, at, '"'))
        {
            return -1;
        }

        var key = Read(json, at + 1);
        var colon = key.Text.Complete ? Expect(json, Skip(json, key.End), ':') : -1;

        if (colon < 0)
        {
            return -1;
        }

        var end = Value(json, Skip(json, colon), key.Text.Value, found);

        return end < 0 ? -1 : Expect(json, Skip(json, end), ',');
    }

    private static int Value(string json, int at, string key, Dictionary<string, PartialText> found)
    {
        if (!Starts(json, at, '"'))
        {
            return Past(json, at);
        }

        var value = Read(json, at + 1);
        found[key] = value.Text;

        return value.Text.Complete ? value.End : -1;
    }

    private static int Expect(string json, int at, char expected) => Starts(json, at, expected) ? at + 1 : -1;

    private static bool Starts(string json, int at, char expected) => at >= 0 && at < json.Length && json[at] == expected;

    private static int Skip(string json, int at)
    {
        while (at >= 0 && at < json.Length && char.IsWhiteSpace(json[at]))
        {
            at++;
        }

        return at;
    }

    private static int Past(string json, int at)
    {
        var depth = 0;

        while (at >= 0 && at < json.Length)
        {
            switch (json[at])
            {
                case '"':
                    var text = Read(json, at + 1);
                    at = text.Text.Complete ? text.End : -1;
                    continue;
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']' when depth > 0:
                    depth--;
                    break;
                case ',' or '}' when depth == 0:
                    return at;
            }

            at++;
        }

        return -1;
    }

    private static (PartialText Text, int End) Read(string json, int at)
    {
        var text = new StringBuilder();

        while (at >= 0 && at < json.Length && json[at] != '"')
        {
            at = json[at] == '\\' ? Escape(json, at, text) : Append(text, json[at], at);
        }

        return at >= 0 && at < json.Length ? (new PartialText(text.ToString(), true), at + 1) : (new PartialText(text.ToString(), false), json.Length);
    }

    private static int Append(StringBuilder text, char next, int at)
    {
        text.Append(next);

        return at + 1;
    }

    private static int Escape(string json, int at, StringBuilder text)
    {
        if (at + 1 >= json.Length)
        {
            return -1;
        }

        var escape = json[at + 1];

        if (escape != 'u')
        {
            text.Append(escape switch { 'n' => '\n', 't' => '\t', 'r' => '\r', 'b' => '\b', 'f' => '\f', _ => escape });

            return at + 2;
        }

        if (Unit(json, at) is not { } unit)
        {
            return -1;
        }

        if (!char.IsHighSurrogate(unit))
        {
            text.Append(unit);

            return at + 6;
        }

        if (Unit(json, at + 6) is not { } low)
        {
            return -1;
        }

        text.Append(unit).Append(low);

        return at + 12;
    }

    private static char? Unit(string json, int at) =>
        at + 6 <= json.Length && json[at] == '\\' && json[at + 1] == 'u'
        && ushort.TryParse(json.AsSpan(at + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit)
            ? (char)unit
            : null;
}
