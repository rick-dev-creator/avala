using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Capturing;
using Avala.Recording.Settings;
using Avala.Recording.Storage;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Recording;

public sealed class RecordingPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.recording", "Recording");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton<IRecordingSettings, SettingsFile>()
            .AddSingleton<IRecordingStore, RecordingFiles>()
            .AddSingleton<IEditedFiles, EditedFiles>()
            .AddSingleton<IAgentProviderDecorator, ProviderRecorder>();
    }
}
