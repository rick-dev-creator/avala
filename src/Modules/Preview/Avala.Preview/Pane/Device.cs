using System.Collections.Immutable;

namespace Avala.Preview.Pane;

internal sealed record Device(string Name, int Width, int Height)
{
    public static Device Phone { get; } = new("iPhone", 393, 852);

    public static Device Android { get; } = new("Android", 412, 915);

    public static Device Tablet { get; } = new("Tablet", 820, 1180);

    public static ImmutableArray<Device> All { get; } = [Phone, Android, Tablet];

    public Device Turn() => this with { Width = Height, Height = Width };
}
