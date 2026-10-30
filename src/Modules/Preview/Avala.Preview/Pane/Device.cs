using System.Collections.Immutable;

namespace Avala.Preview.Pane;

internal enum DeviceShape
{
    Island,
    Punch,
    Classic,
    Fold,
    Tablet,
}

internal sealed record DeviceInsets(double Left, double Top, double Right, double Bottom)
{
    public static DeviceInsets None { get; } = new(0, 0, 0, 0);

    public static DeviceInsets Even(double bezel) => new(bezel, bezel, bezel, bezel);

    public DeviceInsets Turn() => this with { Left = Top, Top = Right, Right = Bottom, Bottom = Left };
}

internal sealed record Device(string Name, string Model, DeviceShape Shape, int Width, int Height, DeviceInsets Insets)
{
    public static ImmutableArray<Device> All { get; } =
    [
        new("iPhone SE", "iphone-se", DeviceShape.Classic, 375, 667, new DeviceInsets(12, 70, 12, 70)),
        new("iPhone 16", "iphone-16", DeviceShape.Island, 393, 852, DeviceInsets.Even(12)),
        new("iPhone 16 Pro Max", "iphone-16-pro-max", DeviceShape.Island, 440, 956, DeviceInsets.Even(11)),
        new("Pixel 9", "pixel-9", DeviceShape.Punch, 412, 915, DeviceInsets.Even(10)),
        new("iPhone Duo", "iphone-duo", DeviceShape.Fold, 772, 548, DeviceInsets.Even(14)),
        new("iPhone Duo (closed)", "iphone-duo-closed", DeviceShape.Punch, 380, 548, DeviceInsets.Even(12)),
        new("iPad Air 11\"", "ipad-air-11", DeviceShape.Tablet, 820, 1180, DeviceInsets.Even(20)),
    ];

    public static Device Named(string name) => All.Single(device => device.Name == name);

    public Device Turn() => this with { Width = Height, Height = Width, Insets = Insets.Turn() };
}
