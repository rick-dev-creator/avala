using Avala.Forges.Contracts;
using Avala.Gitea.Api;
using Avala.Gitea.SignIn;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Gitea;

public sealed class GiteaPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.gitea", "Gitea and Forgejo");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services
            .AddSingleton<IForge>(new GiteaForge(GiteaDialect.Gitea))
            .AddSingleton<IForge>(new GiteaForge(GiteaDialect.Forgejo))
            .AddSingleton<IForgeLogins, TeaLogins>();
}
