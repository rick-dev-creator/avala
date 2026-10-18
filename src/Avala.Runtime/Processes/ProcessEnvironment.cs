using Avala.Sdk;

namespace Avala.Runtime.Processes;

internal sealed class ProcessEnvironment : IEnvironmentVariables
{
    public Option<string> Read(string name) => Environment.GetEnvironmentVariable(name).ToOption();
}
