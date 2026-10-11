namespace Avala.Sdk.Diagnostics;

public interface IRedaction
{
    string Apply(string text);
}
