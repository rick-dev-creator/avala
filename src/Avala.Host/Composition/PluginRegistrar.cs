using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Composition;

internal sealed class PluginRegistrar(IServiceCollection services) : IPluginRegistrar
{
    public IServiceCollection Services { get; } = services;
}
