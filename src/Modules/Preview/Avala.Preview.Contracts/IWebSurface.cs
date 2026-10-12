using Avala.Sdk;

namespace Avala.Preview.Contracts;

public enum SurfaceEmbedding
{
    Embedded,
    Window,
    None,
}

public sealed record SurfaceRules(Func<Uri, bool> Admits, Func<Uri, bool> AdmitsResource);

public interface IWebSurface
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

public interface IWebSurfaces
{
    IWebSurface Open(SurfaceRules rules);
}
