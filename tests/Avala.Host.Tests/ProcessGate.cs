using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class ProcessGate : IPlugin
{
    public PluginInfo Info { get; } = new("process-gate", "Process gate");

    private readonly List<string> violations = [];

    public ProcessesInFlight InFlight { get; } = new();

    public Option<InMemoryGit> Watched { get; set; }

    public IReadOnlyList<string> Violations
    {
        get
        {
            lock (violations)
            {
                return [.. violations];
            }
        }
    }

    public void Register(IPluginRegistrar registrar)
    {
        var real = registrar.Services.Last(service => service.ServiceType == typeof(IProcessRunner));
        registrar.Services.Remove(real);
        registrar.Services.AddSingleton(provider => InFlight.Counting(Watched.Match<IProcessRunner>(git => new GitGuard(Real(provider, real), git, this), () => Real(provider, real))));
    }

    private void Violated(ProcessRequest request)
    {
        lock (violations)
        {
            violations.Add($"git {string.Join(' ', request.Arguments)} ran on a repository whose git is in memory; mark the test [UsesRealGit] if it proves git's own behavior, or prove the behavior without git.");
        }
    }

    private sealed class GitGuard(IProcessRunner inner, InMemoryGit git, ProcessGate gate) : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            if (Path.GetFileNameWithoutExtension(request.FileName) == "git" && request.Arguments is not ["--version", ..] && Folders(request).Any(git.Holds))
            {
                gate.Violated(request);
            }

            return inner.RunAsync(request, cancellationToken);
        }

        private static IEnumerable<string> Folders(ProcessRequest request) =>
        [
            .. request.WorkingDirectory.Match<string[]>(folder => [folder], () => [Environment.CurrentDirectory]),
            .. request.Arguments.Zip(request.Arguments.Skip(1)).Where(pair => pair.First == "-C").Select(pair => pair.Second),
        ];
    }

    private static IProcessRunner Real(IServiceProvider provider, ServiceDescriptor real) =>
        real.ImplementationInstance as IProcessRunner
        ?? real.ImplementationFactory?.Invoke(provider) as IProcessRunner
        ?? (IProcessRunner)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!);
}
