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

        private static readonly string[] Wrappers = ["env", "xargs", "nice", "nohup", "time", "command", "exec", "sudo"];

        private static readonly string[] Variables = ["GIT_DIR", "GIT_WORK_TREE"];

        private static readonly string[] Locations = ["--git-dir", "--work-tree"];

        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            if (RunsGit(request) && Folders(request).Any(git.Holds))
            {
                gate.Violated(request);
            }

            return inner.RunAsync(request, cancellationToken);
        }

        private static bool RunsGit(ProcessRequest request) =>
            Program(request.FileName) switch
            {
                "git" => request.Arguments is not ["--version", ..],
                var program when Wrappers.Contains(program, StringComparer.Ordinal) => request.Arguments.Any(argument => Program(argument) == "git"),
                var program when Shells.Contains(program, StringComparer.OrdinalIgnoreCase) => request.Arguments.Any(argument => GitWord().IsMatch(argument)),
                _ => false,
            };

        private static string Program(string name) => Path.GetFileNameWithoutExtension(name);

        private static IEnumerable<string> Folders(ProcessRequest request)
        {
            var current = request.WorkingDirectory.Match(folder => folder, () => Environment.CurrentDirectory);
            var folders = new List<string> { current };

            for (var index = 0; index < request.Arguments.Count; index++)
            {
                var argument = request.Arguments[index];
                var next = index + 1 < request.Arguments.Count ? request.Arguments[index + 1] : null;

                if (argument == "-C" && next is not null)
                {
                    current = Path.GetFullPath(next, current);
                    folders.Add(current);
                }
                else if (Locations.Contains(argument, StringComparer.Ordinal) && next is not null)
                {
                    folders.Add(Path.GetFullPath(next, current));
                }
                else if (Locations.Any(location => argument.StartsWith(location + "=", StringComparison.Ordinal)))
                {
                    folders.Add(Path.GetFullPath(argument[(argument.IndexOf('=') + 1)..], current));
                }

                folders.AddRange(Mentioned(argument, current));
            }

            folders.AddRange(Variables.Where(request.Environment.ContainsKey).Select(name => Path.GetFullPath(request.Environment[name], current)));

            return folders.Select(folder => Path.GetFileName(folder) == ".git" ? Path.GetDirectoryName(folder)! : folder);
        }

        private static IEnumerable<string> Mentioned(string argument, string current) =>
        [
            .. Quoted().Matches(argument).Select(found => found.Groups["path"].Value).Where(Path.IsPathFullyQualified),
            .. AbsolutePath().Matches(argument).Select(found => found.Value.Trim('\'', '"')),
            .. ChangedInto().Matches(argument).Select(found => Path.GetFullPath(found.Groups["path"].Value.Trim('\'', '"'), current)),
        ];

        [GeneratedRegex(@"(^|[\s;&|(""'/`])git(\.exe)?($|[\s""'`;)&|])")]
        private static partial Regex GitWord();

        [GeneratedRegex(@"(?:[A-Za-z]:)?[/\\][^\s'"";&|]+")]
        private static partial Regex AbsolutePath();

        [GeneratedRegex(@"(?<q>['""])(?<path>[^'""]+)\k<q>")]
        private static partial Regex Quoted();

        [GeneratedRegex(@"(^|[\s;&|(])cd\s+(?<path>""[^""]+""|'[^']+'|[^\s;&|]+)")]
        private static partial Regex ChangedInto();
    }

    private static IProcessRunner Real(IServiceProvider provider, ServiceDescriptor real) =>
        real.ImplementationInstance as IProcessRunner
        ?? real.ImplementationFactory?.Invoke(provider) as IProcessRunner
        ?? (IProcessRunner)ActivatorUtilities.CreateInstance(provider, real.ImplementationType!);
}
