using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Testing;

public sealed class PluginComposition : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly IReadOnlyDictionary<Type, int> registered;

    private PluginComposition(ServiceProvider services, IReadOnlyDictionary<Type, int> registered)
    {
        this.services = services;
        this.registered = registered;
    }

    public static PluginComposition Of(IPlugin plugin, AvalaPaths paths, Action<IServiceCollection> consumed)
    {
        var services = new ServiceCollection()
            .AddSingleton(paths)
            .AddSingleton<IEventBus>(new RecordingBus())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        consumed(services);
        var before = services.Count;
        plugin.Register(new Registrar(services));
        var contributed = services.Skip(before).Select(service => service.ServiceType).ToHashSet();
        var registered = services.Where(service => contributed.Contains(service.ServiceType))
            .CountBy(service => service.ServiceType)
            .ToDictionary();

        return new PluginComposition(
            services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }),
            registered);
    }

    public IReadOnlyDictionary<Type, int> Registered => registered;

    public IReadOnlyDictionary<Type, int> ResolveEveryRegistration() =>
        registered.Keys.ToDictionary(type => type, type => services.GetServices(type).OfType<object>().Count());

    public T Get<T>()
        where T : notnull =>
        services.GetRequiredService<T>();

    public IReadOnlyList<T> All<T>()
        where T : notnull =>
        [.. services.GetServices<T>()];

    public ValueTask DisposeAsync() => services.DisposeAsync();

    private sealed class Registrar(IServiceCollection services) : IPluginRegistrar
    {
        public IServiceCollection Services { get; } = services;
    }
}
