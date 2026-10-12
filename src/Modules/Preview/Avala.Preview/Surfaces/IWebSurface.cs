using Avala.Sdk;

namespace Avala.Preview.Surfaces;

internal enum SurfaceEmbedding
{
    Embedded,
    Window,
    None,
}

internal sealed record SurfaceRules(Func<Uri, bool> Admits, Func<Uri, bool> AdmitsResource);

internal interface IWebSurface
{
    SurfaceEmbedding Embedding { get; }

    Option<Uri> Address { get; }

    bool CanGoBack { get; }

    event EventHandler<Uri>? Refused;

    event EventHandler? Changed;

    void Navigate(Uri address);

    void Reload();

    void GoBack();

    void Close();
}

internal interface IWebSurfaces
{
    IWebSurface Open(SurfaceRules rules);
}
