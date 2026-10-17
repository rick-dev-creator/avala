using Avala.Documentation.RepositoryFiles;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Documentation;

public sealed class DocumentationPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.documentation", "Documentation");

    public void Register(IPluginRegistrar registrar) =>
        registrar.Services.AddSingleton<DocsRulesReader>();
}
