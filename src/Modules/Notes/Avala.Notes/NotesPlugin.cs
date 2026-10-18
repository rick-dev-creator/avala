using Avala.Notes.Contracts;
using Avala.Notes.NoteFiles;
using Avala.Notes.Taking;
using Avala.Sdk;
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
            .AddForwarded<INotes, NoteBook>();
    }
}
