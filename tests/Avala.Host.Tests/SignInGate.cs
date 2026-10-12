using Avala.Sdk;
using Avala.Sdk.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class SignInGate : IPlugin
{
    private static readonly string[] SignInTools = ["gh", "tea"];

    public PluginInfo Info { get; } = new("sign-in-gate", "Sign-in gate");

    public Dictionary<string, ProcessOutcome> Recorded { get; } = new(StringComparer.Ordinal);

    public void Register(IPluginRegistrar registrar)
    {
        var real = registrar.Services.Last(service => service.ServiceType == typeof(IProcessRunner));
        registrar.Services.Remove(real);
        registrar.Services.AddSingleton<IProcessRunner>(provider => new RecordedTools(Real(provider, real), Recorded));
    }

    private static IProcessRunner Real(IServiceProvider provider, ServiceDescriptor real) =>
        real.ImplementationInstance as IProcessRunner
        ?? real.ImplementationFactory?.Invoke(provider) as IProcessRunner
        ?? (IProcessRunner)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!);

    private sealed class RecordedTools(IProcessRunner real, IReadOnlyDictionary<string, ProcessOutcome> recorded) : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            SignInTools.Contains(request.FileName, StringComparer.Ordinal)
                ? ValueTask.FromResult(recorded.TryGetValue(request.FileName, out var outcome) ? Result<ProcessOutcome, ProcessError>.Success(outcome) : ProcessError.NotFound)
                : real.RunAsync(request, cancellationToken);
    }
}
