using Avala.Sdk;

namespace Avala.Preview.UI;

public sealed class PreviewPlugin : IPlugin
{
    public PluginInfo Info { get; } = new("avala.preview", "Preview");

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddPreview();
}
