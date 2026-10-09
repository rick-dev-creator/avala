using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Cli;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Folders;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.ClaudeCode;

public sealed class ClaudeCodePlugin(string executable, IReadOnlyList<string> arguments) : IPlugin
{
    public ClaudeCodePlugin()
        : this(CliCommand.Installed.FileName, CliCommand.Installed.Prefix)
    {
    }

    public PluginInfo Info { get; } = new("avala.claude-code", "Claude Code");

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.TryAddSingleton(TimeProvider.System);
        registrar.Services
            .AddSingleton(new CliCommand(executable, arguments))
            .AddSingleton<ICli, ProcessCli>()
            .AddSingleton<IConfigurationFolders>(_ => new ConfigurationFolders())
            .AddSingleton<IAgentProvider, ClaudeCodeProvider>();
    }
}
