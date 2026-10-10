using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.ClaudeCode.Conversations;

internal interface ICli
{
    Result<ICliProcess, AgentError> Start(CliLaunch launch, IProcessLauncher processes, Option<string> transcripts);
}

internal interface ICliProcess : IAsyncDisposable
{
    IAsyncEnumerable<string> Lines { get; }

    Task WriteAsync(string line, CancellationToken cancellationToken);
}

internal interface IConfigurationFolders
{
    Task<Option<AgentAccount>> AccountAsync(ConnectionEnvironment connection, CancellationToken cancellationToken);

    bool Holds(ConnectionEnvironment connection, Guid conversation);

    Task<IReadOnlyList<JsonNode>> EarlierAsync(ConnectionEnvironment connection, Guid conversation, CancellationToken cancellationToken);
}
