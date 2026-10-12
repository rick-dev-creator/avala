namespace Avala.Sdk.Presentation;

public sealed class LocalPageRequested(Uri page)
{
    public Uri Page => page;

    public bool IsShown { get; private set; }

    public void Show() => IsShown = true;
}
