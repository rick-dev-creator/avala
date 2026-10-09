using Avala.Agents.Connections;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Microsoft.Extensions.Logging;

namespace Avala.Agents.Sessions;

internal sealed partial class SessionStarter(
    ConnectionRegistry connections,
    ProviderChain chain,
    IProcessTrees trees,
    ILogger<SessionStarter> logger)
{
    public async Task<Result<StartedSession, AgentError>> StartAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (!(await connections.ResolveAsync(request.Connection, cancellationToken)).TryGetValue(out var connection, out var error))
        {
            LogUnavailable(request.Connection.Match(name => name.Value, () => "the default connection"), error);

            return Unavailable(error);
        }

        var provider = chain.Decorate(connection.Provider);
        var tree = await trees.OpenAsync(request.WorkingDirectory, cancellationToken);

        var fresh = new SessionOptions(request.WorkingDirectory, PermissionMode.AskEveryTime)
        {
            Tools = chain.ToolsFor(provider),
            Connection = connection.Environment,
            Processes = tree,
        };
        var resumed = provider.Capabilities.CanResume
            ? await request.Resume.Match(
                token => ResumeAsync(provider, fresh with { Resume = token }, cancellationToken),
                () => Task.FromResult(Option<IAgentSession>.None))
            : Option<IAgentSession>.None;

        var started = await resumed.Match(
            session => Task.FromResult(Result<StartedSession, AgentError>.Success(new StartedSession(provider, session, connection.Name, tree.Id, Resumed: true))),
            async () => (await provider.StartAsync(fresh, cancellationToken))
                .Map(session => new StartedSession(provider, session, connection.Name, tree.Id, Resumed: false)));

        if (started.IsFailure)
        {
            _ = await trees.CloseAsync(tree.Id, cancellationToken);
        }

        return started;
    }

    private static async Task<Option<IAgentSession>> ResumeAsync(
        IAgentProvider provider,
        SessionOptions options,
        CancellationToken cancellationToken) =>
        (await provider.StartAsync(options, cancellationToken)).Match(Option<IAgentSession>.Some, _ => Option<IAgentSession>.None);

    private static AgentError Unavailable(ConnectionError error) => error switch
    {
        ConnectionError.UnknownConnection => AgentError.UnknownConnection,
        ConnectionError.NoConnections or ConnectionError.UnknownProvider => AgentError.ProviderUnavailable,
        _ => AgentError.UnusableConnection,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot open a session on {Connection}: {Error}")]
    private partial void LogUnavailable(string connection, ConnectionError error);
}

internal sealed class ProviderChain(IEnumerable<HarnessTool> tools, IEnumerable<IAgentProviderDecorator> decorators)
{
    public IAgentProvider Decorate(IAgentProvider provider) =>
        decorators.Aggregate(provider, (inner, decorator) => decorator.Decorate(inner));

    public IReadOnlyList<HarnessTool> ToolsFor(IAgentProvider provider) => provider.Capabilities.AcceptsTools ? [.. tools] : [];
}

internal sealed record StartedSession(IAgentProvider Provider, IAgentSession Session, ConnectionName Connection, ProcessTreeId Tree, bool Resumed);
