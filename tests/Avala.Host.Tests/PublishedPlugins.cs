using Avala.Host.Composition;

[assembly: AssemblyFixture(typeof(Avala.Host.Tests.PublishedPlugins))]

namespace Avala.Host.Tests;

public sealed class PublishedPlugins
{
    public PublishedPlugins() => _ = PluginLoader.Load(Directory);

    public string Directory { get; } = PluginDirectory.Resolve();
}
