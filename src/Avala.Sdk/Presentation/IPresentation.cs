namespace Avala.Sdk.Presentation;

public interface IPresentation
{
    long Revision { get; }

    event EventHandler<Presented>? Presented;
}
