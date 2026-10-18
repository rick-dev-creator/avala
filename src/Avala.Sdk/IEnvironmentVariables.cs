namespace Avala.Sdk;

public interface IEnvironmentVariables
{
    Option<string> Read(string name);
}
