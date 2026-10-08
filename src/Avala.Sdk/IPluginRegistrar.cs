using Microsoft.Extensions.DependencyInjection;

namespace Avala.Sdk;

public interface IPluginRegistrar
{
    IServiceCollection Services { get; }
}
