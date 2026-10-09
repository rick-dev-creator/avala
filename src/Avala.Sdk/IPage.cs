namespace Avala.Sdk;

public interface IPage
{
    string Title { get; }

    string Icon => string.Empty;

    PagePlacement Placement => PagePlacement.Navigation;
}

public enum PagePlacement
{
    Navigation,
    Hidden,
}
