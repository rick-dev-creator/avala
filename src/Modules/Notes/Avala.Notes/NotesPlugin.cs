using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Notes.NoteFiles;
using Avala.Notes.Taking;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Notes;

public sealed class NotesPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.notes", "Notes");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<INoteShelf, NoteShelf>()
            .AddSingleton<NotePlaces>()
            .AddSingleton<NoteBook>()
            .AddForwarded<INotes, NoteBook>()
            .AddSingleton<IIdeas, Ideas>()
            .AddSingleton<NoteCommits>()
            .AddForwarded<IHandle<NoteTaken>, NoteCommits>()
            .AddForwarded<IHandle<NoteChanged>, NoteCommits>()
            .AddForwarded<IHandle<JobSessionStarted>, NoteCommits>()
            .AddForwarded<IHandle<AgentActivity>, NoteCommits>()
            .AddSingleton<IWorkspaceRemovals, NoteKeeping>();
    }
}
