using System.Collections.Immutable;

namespace Avala.Preview.Pane;

internal enum DeviceShape
{
    Phone,
    Android,
    Tablet,
}

internal sealed record Device(string Name, DeviceShape Shape, int Width, int Height, int Bezel)
{
    public static Device Phone { get; } = new("iPhone", DeviceShape.Phone, 393, 852, 12);

    public static Device Android { get; } = new("Android", DeviceShape.Android, 412, 915, 10);

    public static Device Tablet { get; } = new("Tablet", DeviceShape.Tablet, 820, 1180, 20);

    public static ImmutableArray<Device> All { get; } = [Phone, Android, Tablet];

    public Device Turn() => this with { Width = Height, Height = Width };
}
