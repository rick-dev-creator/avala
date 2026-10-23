using System.Text.RegularExpressions;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed partial class ProcessGate : IPlugin
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

    private sealed partial class GitGuard(IProcessRunner inner, InMemoryGit git, ProcessGate gate) : IProcessRunner
    {
        private static readonly string[] Shells = ["sh", "bash", "zsh", "dash", "cmd", "pwsh", "powershell"];

        private static readonly string[] Variables = ["GIT_DIR", "GIT_WORK_TREE"];

        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            if (RunsGit(request) && Folders(request).Any(git.Holds))
            {
                gate.Violated(request);
            }

            return inner.RunAsync(request, cancellationToken);
        }

        private static bool RunsGit(ProcessRequest request) =>
            Program(request) switch
            {
                "git" => request.Arguments is not ["--version", ..],
                var program when Shells.Contains(program, StringComparer.OrdinalIgnoreCase) => request.Arguments.Any(argument => GitWord().IsMatch(argument)),
                _ => false,
            };

        private static string Program(ProcessRequest request) => Path.GetFileNameWithoutExtension(request.FileName);

        private static IEnumerable<string> Folders(ProcessRequest request)
        {
            var current = request.WorkingDirectory.Match(folder => folder, () => Environment.CurrentDirectory);
            var folders = new List<string> { current };

            for (var index = 0; index < request.Arguments.Count; index++)
            {
                var argument = request.Arguments[index];

                if (argument == "-C" && index + 1 < request.Arguments.Count)
                {
                    current = Path.GetFullPath(request.Arguments[index + 1], current);
                    folders.Add(current);
                }
                else if (argument.StartsWith("--git-dir=", StringComparison.Ordinal) || argument.StartsWith("--work-tree=", StringComparison.Ordinal))
                {
                    folders.Add(Path.GetFullPath(argument[(argument.IndexOf('=') + 1)..], current));
                }

                folders.AddRange(AbsolutePath().Matches(argument).Select(found => found.Value.Trim('\'', '"')));
            }

            folders.AddRange(Variables.Where(request.Environment.ContainsKey).Select(name => Path.GetFullPath(request.Environment[name], current)));

            return folders.Select(folder => Path.GetFileName(folder) == ".git" ? Path.GetDirectoryName(folder)! : folder);
        }

        [GeneratedRegex(@"(^|[\s;&|(])git(\.exe)?($|\s)")]
        private static partial Regex GitWord();

        [GeneratedRegex(@"(?:[A-Za-z]:)?[/\\][^\s'"";&|]+")]
        private static partial Regex AbsolutePath();
    }

    private static IProcessRunner Real(IServiceProvider provider, ServiceDescriptor real) =>
        real.ImplementationInstance as IProcessRunner
        ?? real.ImplementationFactory?.Invoke(provider) as IProcessRunner
        ?? (IProcessRunner)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!);
}
