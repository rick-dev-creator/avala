using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Host.Tests;

internal sealed class EnvironmentGate : IPlugin
{
    public PluginInfo Info { get; } = new("environment-gate", "Environment gate");

    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.RemoveAll<IEnvironmentVariables>();
        registrar.Services.AddSingleton<IEnvironmentVariables>(new Controlled(Variables));
    }

    private sealed class Controlled(IReadOnlyDictionary<string, string> variables) : IEnvironmentVariables
    {
        public Option<string> Read(string name) => variables.TryGetValue(name, out var value) ? value : Option<string>.None;
    }
}
