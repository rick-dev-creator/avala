using Avala.Agents.Contracts;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Transcripts.Contracts;
using Avala.Transcripts.Keeping;
using Avala.Transcripts.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Transcripts;

public sealed class TranscriptsPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.transcripts", "Transcripts");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<SqliteTranscriptLog>()
            .AddForwarded<ITranscriptLog, SqliteTranscriptLog>()
            .AddForwarded<IStartupTask, SqliteTranscriptLog>()
            .AddSingleton<ITranscripts, TranscriptBook>()
            .AddSingleton<TranscriptKeeper>()
            .AddForwarded<IHandle<JobSessionStarted>, TranscriptKeeper>()
            .AddForwarded<IHandle<JobProgressed>, TranscriptKeeper>()
            .AddForwarded<IHandle<AgentActivity>, TranscriptKeeper>()
            .AddForwarded<IHandle<CanvasUpdated>, TranscriptKeeper>()
            .AddForwarded<IHandle<PermissionDecided>, TranscriptKeeper>()
            .AddForwarded<IHandle<FormDecided>, TranscriptKeeper>();
    }
}
