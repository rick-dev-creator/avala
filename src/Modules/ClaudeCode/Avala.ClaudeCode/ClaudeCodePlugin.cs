using System.Collections;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Cli;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Discovery;
using Avala.ClaudeCode.Folders;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.ClaudeCode;

public sealed class ClaudeCodePlugin(string executable, IReadOnlyList<string> arguments, string home, IReadOnlyDictionary<string, string> environment) : IPlugin
{
    public ClaudeCodePlugin()
        : this(CliCommand.Installed.FileName, CliCommand.Installed.Prefix, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Variables())
    {
    }

    public ClaudeCodePlugin(string executable, IReadOnlyList<string> arguments)
        : this(executable, arguments, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), [])
    {
    }

    public ClaudeCodePlugin(string executable, IReadOnlyList<string> arguments, string home, IReadOnlyList<string> configured)
        : this(executable, arguments, home, Configured(configured))
    {
    }

    public PluginInfo Info { get; } = new("avala.claude-code", "Claude Code");

    public void Register(IPluginRegistrar registrar)
    {
        IReadOnlyList<string> configured = environment.TryGetValue(CommandLine.ConfigurationVariable, out var folder) && folder.Length > 0 ? [folder] : [];
        var user = new UserHome(Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)), configured);
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton(new CliCommand(executable, arguments))
            .AddSingleton(user)
            .AddSingleton(new ImplicitSignIn(new SignIn(user, environment).IsAvailable))
            .AddSingleton<ICli, ProcessCli>()
            .AddSingleton<IConfigurationFolders, ConfigurationFolders>()
            .AddSingleton<IConnectionDiscovery, LoginFolders>()
            .AddSingleton<IAgentProvider, ClaudeCodeProvider>();
    }

    private static Dictionary<string, string> Configured(IReadOnlyList<string> configured)
    {
        var variables = Variables();
        variables.Remove(CommandLine.ConfigurationVariable);

        if (configured is [var folder, ..])
        {
            variables[CommandLine.ConfigurationVariable] = folder;
        }

        return variables;
    }

    private static Dictionary<string, string> Variables() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => entry.Value as string ?? string.Empty);
}
