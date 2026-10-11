using System.Reflection;
using Avala.Runtime;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Composition;

internal static class TelemetryBuild
{
    public const string KeyProperty = "AvalaTelemetryKey";

    public const string HostProperty = "AvalaTelemetryHost";

    public static Option<string> Key { get; } = Metadata(KeyProperty);

    public static Option<string> SelfHostedHost { get; } = Metadata(HostProperty);

    extension(IServiceCollection services)
    {
        public IServiceCollection AddNetwork(Func<HttpMessageHandler> network)
        {
            services.AddReleaseFeed(network());

            return Key.Match(key => services.AddTelemetrySender(network(), key, SelfHostedHost), () => services);
        }
    }

    private static Option<string> Metadata(string name) =>
        typeof(TelemetryBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == name)?.Value is { Length: > 0 } value
            ? value.Trim()
            : Option<string>.None;
}
