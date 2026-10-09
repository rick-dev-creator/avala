using Avalonia.Data.Converters;

namespace Avala.Workbench.UI;

internal static class Answers
{
    public static IValueConverter Is { get; } = new FuncValueConverter<object?, string?, bool>((value, name) => string.Equals(value?.ToString(), name, StringComparison.Ordinal));
}
