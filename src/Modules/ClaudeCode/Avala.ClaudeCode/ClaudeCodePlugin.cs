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

public sealed class ClaudeCodePlugin(string executable, IReadOnlyList<string> arguments, string home, IReadOnlyList<string> configured) : IPlugin
{
    public ClaudeCodePlugin()
        : this(
            CliCommand.Installed.FileName,
            CliCommand.Installed.Prefix,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable(CommandLine.ConfigurationVariable) is { Length: > 0 } folder ? [folder] : [])
    {
    }

    public ClaudeCodePlugin(string executable, IReadOnlyList<string> arguments)
        : this(executable, arguments, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), [])
    {
    }

    public PluginInfo Info { get; } = new("avala.claude-code", "Claude Code");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton(new CliCommand(executable, arguments))
            .AddSingleton(new UserHome(Path.TrimEndingDirectorySeparator(Path.GetFullPath(home)), configured))
            .AddSingleton<ICli, ProcessCli>()
            .AddSingleton<IConfigurationFolders, ConfigurationFolders>()
            .AddSingleton<IConnectionDiscovery, LoginFolders>()
            .AddSingleton<IAgentProvider, ClaudeCodeProvider>();
    }
}
