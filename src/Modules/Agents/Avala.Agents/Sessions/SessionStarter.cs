using Avala.Agents.Connections;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Agents.Sessions;

internal sealed partial class SessionStarter(
    ConnectionRegistry connections,
    IEnumerable<HarnessTool> tools,
    IEnumerable<IAgentProviderDecorator> decorators,
    ILogger<SessionStarter> logger)
{
    public async Task<Result<StartedSession, AgentError>> StartAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (!(await connections.ResolveAsync(request.Connection, cancellationToken)).TryGetValue(out var connection, out var error))
        {
            LogUnavailable(request.Connection.Match(name => name.Value, () => "the default connection"), error);

            return Unavailable(error);
        }

        var provider = decorators.Aggregate(connection.Provider, (inner, decorator) => decorator.Decorate(inner));

        var fresh = new SessionOptions(request.WorkingDirectory, PermissionMode.AskEveryTime)
        {
            Tools = provider.Capabilities.AcceptsTools ? [.. tools] : [],
            Connection = connection.Environment,
        };
        var resumed = provider.Capabilities.CanResume
            ? await request.Resume.Match(
                token => ResumeAsync(provider, fresh with { Resume = token }, connection.Name, cancellationToken),
                () => Task.FromResult(Option<StartedSession>.None))
            : Option<StartedSession>.None;

        return await resumed.Match(
            started => Task.FromResult(Result<StartedSession, AgentError>.Success(started)),
            async () => (await provider.StartAsync(fresh, cancellationToken))
                .Map(session => new StartedSession(provider, session, connection.Name, Resumed: false)));
    }

    private static async Task<Option<StartedSession>> ResumeAsync(
        IAgentProvider provider,
        SessionOptions options,
        ConnectionName connection,
        CancellationToken cancellationToken) =>
        (await provider.StartAsync(options, cancellationToken)).Match(
            session => Option<StartedSession>.Some(new StartedSession(provider, session, connection, Resumed: true)),
            _ => Option<StartedSession>.None);

    private static AgentError Unavailable(ConnectionError error) => error switch
    {
        ConnectionError.UnknownConnection => AgentError.UnknownConnection,
        ConnectionError.NoConnections or ConnectionError.UnknownProvider => AgentError.ProviderUnavailable,
        _ => AgentError.UnusableConnection,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot open a session on {Connection}: {Error}")]
    private partial void LogUnavailable(string connection, ConnectionError error);
}

internal sealed record StartedSession(IAgentProvider Provider, IAgentSession Session, ConnectionName Connection, bool Resumed);
