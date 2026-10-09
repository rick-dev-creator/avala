using Avalonia.Data.Converters;

namespace Avala.Components.UI.States;

public static class Texts
{
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

    public static IValueConverter Leaf { get; } = new FuncValueConverter<string, string>(path =>
    {
        var trimmed = (path ?? string.Empty).Trim().TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(['/', '\\']);

        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    });
}
