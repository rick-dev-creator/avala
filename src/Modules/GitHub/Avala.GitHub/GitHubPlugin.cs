using Avala.Forges.Contracts;
using Avala.GitHub.Api;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.GitHub;

public sealed class GitHubPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.github", "GitHub");

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddSingleton<IForge, GitHubForge>();
}
