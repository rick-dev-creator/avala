namespace Avala.Sdk;

public interface IPlugin
{
    PluginInfo Info { get; }

    void Register(IPluginRegistrar registrar);
}
