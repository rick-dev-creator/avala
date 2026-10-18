using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace Avala.Components.UI.States;

public static partial class Texts
{
    public static IValueConverter Unbroken { get; } = new FuncValueConverter<string, string>(text => Typeset(text ?? string.Empty));

    public static IMultiValueConverter Same { get; } = new FuncMultiValueConverter<object?, bool>(values =>
    {
        var all = values.ToList();

        return all.Count > 1 && all.Skip(1).All(value => Equals(value, all[0]));
    });

    public static IMultiValueConverter Differ { get; } = new FuncMultiValueConverter<object?, bool>(values =>
    {
        var all = values.ToList();

        return all.Count > 1 && all.Skip(1).Any(value => !Equals(value, all[0]));
    });

    public static IValueConverter Sentence { get; } = new FuncValueConverter<string, string>(text =>
        string.IsNullOrEmpty(text) ? string.Empty : string.Concat(char.ToUpperInvariant(text[0]).ToString(), text[1..]));

    public static IValueConverter Leaf { get; } = new FuncValueConverter<string, string>(path =>
    {
        var trimmed = (path ?? string.Empty).Trim().TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(['/', '\\']);

        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    });

    public static string Typeset(string text) =>
        Joined().Replace(Measured().Replace(Dated().Replace(text.Replace(" · ", "\u00A0· ", StringComparison.Ordinal), date => date.Value.Replace(' ', '\u00A0')), "\u00A0"), "-\u2060");

    [GeneratedRegex(@"\b(?:(?:Mon|Tue|Wed|Thu|Fri|Sat|Sun) \d{1,2} (?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)(?: \d{2}:\d{2})?|\d{4}-\d{2}-\d{2} \d{2}:\d{2})\b")]
    private static partial Regex Dated();

    [GeneratedRegex(@"(?<=\d%?) (?=\p{L})|(?<=\b\p{L}{1,2}) (?=\d)")]
    private static partial Regex Measured();

    [GeneratedRegex(@"(?<=[\p{L}\d])-(?=[\p{L}\d])")]
    private static partial Regex Joined();
}
