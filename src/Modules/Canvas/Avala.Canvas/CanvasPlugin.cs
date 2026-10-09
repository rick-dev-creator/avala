using Avala.Agents.Contracts;
using Avala.Canvas.Contracts;
using Avala.Canvas.Drawing;
using Avala.Canvas.Gallery;
using Avala.Canvas.Streaming;
using Avala.Canvas.Throttling;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Canvas;

public sealed class CanvasPlugin : IPlugin
{
    private static readonly TimeSpan SnapshotInterval = TimeSpan.FromMilliseconds(100);

    public PluginInfo Info { get; } = new("avala.canvas", "Canvas");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton(provider => new CanvasOffer(provider.GetServices<CanvasFormat>()))
            .AddSingleton<CanvasGallery>()
            .AddSingleton<ICanvases>(provider => provider.GetRequiredService<CanvasGallery>())
            .AddSingleton(provider => new SnapshotThrottle(
                provider.GetRequiredService<CanvasGallery>(),
                provider.GetRequiredService<IEventBus>(),
                provider.GetRequiredService<TimeProvider>(),
                SnapshotInterval))
            .AddSingleton<IHandle<AgentActivity>, CanvasFeed>()
            .AddSingleton(provider => CanvasTool.For(provider.GetRequiredService<CanvasOffer>()));
    }
}
