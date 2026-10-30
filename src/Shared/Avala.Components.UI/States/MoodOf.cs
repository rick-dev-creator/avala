using System.Globalization;
using Avala.Components.Status;
using Avalonia.Data.Converters;

namespace Avala.Components.UI.States;

public sealed class MoodOf : IValueConverter
{
    public static MoodOf Status { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StatusKind status ? Moods.Of(status) : Mood.Focused;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
