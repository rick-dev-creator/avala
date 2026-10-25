using System.Globalization;
using Avala.Preview.Origins;

namespace Avala.Preview.Navigation;

internal sealed record DeviceView(string Model, int Width, int Height, bool Turned, bool Deep)
{
    public string Page => FileAddresses.ViewerFolder + (Deep ? "device.html" : "screen.html");

    public Uri Around(Uri viewer, Uri page, int version) =>
        new(viewer, string.Create(
            CultureInfo.InvariantCulture,
            $"{viewer.AbsolutePath}?device={Uri.EscapeDataString(Model)}&w={Width}&h={Height}&turned={(Turned ? 1 : 0)}&page={Uri.EscapeDataString(page.AbsoluteUri)}&v={version}"));
}
