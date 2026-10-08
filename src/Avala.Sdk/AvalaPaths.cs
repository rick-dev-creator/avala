namespace Avala.Sdk;

public sealed record AvalaPaths(string Data)
{
    public string Database(string module) => Path.Combine(Data, $"{module}.db");

    public string Folder(string name) => Path.Combine(Data, name);
}
