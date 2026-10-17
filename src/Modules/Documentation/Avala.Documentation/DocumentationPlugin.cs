using Avala.Agents.Contracts;
using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Records;
using Avala.Documentation.RepositoryFiles;
using Avala.Documentation.Storage;
using Avala.Documentation.Writing;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Documentation;

public sealed class DocumentationPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.documentation", "Documentation");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<IDocsRules, DocsRulesReader>()
            .AddSingleton<FeatureCatalog>()
            .AddSingleton<SqliteDocumentationStore>()
            .AddForwarded<IDocumentationStore, SqliteDocumentationStore>()
            .AddForwarded<IStartupTask, SqliteDocumentationStore>()
            .AddSingleton<DocumentationBook>()
            .AddForwarded<IStartupTask, DocumentationBook>()
            .AddSingleton<JobPlaces>()
            .AddSingleton<DocumentedJobs>()
            .AddSingleton<WriterLauncher>()
            .AddSingleton<VersionKeeper>()
            .AddSingleton<DocumentationDesk>()
            .AddForwarded<IHandle<JobSessionStarted>, DocumentationDesk>()
            .AddForwarded<IHandle<AgentActivity>, DocumentationDesk>()
            .AddForwarded<IHandle<JobProgressed>, DocumentationDesk>()
            .AddForwarded<IHandle<StartupCompleted>, DocumentationDesk>()
            .AddSingleton<DocumentationReads>()
            .AddSingleton<Docfx>()
            .AddSingleton<SiteFiller>()
            .AddSingleton<SiteDesk>()
            .AddSingleton<DocumentationEntry>()
            .AddForwarded<IDocumentation, DocumentationEntry>();
    }
}
