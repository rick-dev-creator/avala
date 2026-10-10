using Avala.Agents.Connections;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
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
        var capabilities = provider.CapabilitiesOn(connection.Environment);

        return await OffersModels.RefusalOn(capabilities, request.Model).Match(
            refusal => Task.FromResult(Refused(connection.Name, refusal)),
            () => OpenAsync(request, connection, provider, capabilities, cancellationToken));
    }

    private Result<StartedSession, AgentError> Refused(ConnectionName connection, ModelRefusal refusal)
    {
        LogUnoffered(connection.Value, refusal);

        return refusal == ModelRefusal.UnofferedModel ? AgentError.UnofferedModel : AgentError.UnofferedEffort;
    }

    private async Task<Result<StartedSession, AgentError>> OpenAsync(
        AgentRequest request,
        ResolvedConnection connection,
        IAgentProvider provider,
        CapabilitySet capabilities,
        CancellationToken cancellationToken)
    {
        var tree = await trees.OpenAsync(request.WorkingDirectory, cancellationToken);

        var fresh = new SessionOptions(request.WorkingDirectory, PermissionMode.AskEveryTime)
        {
            Tools = chain.ToolsFor(capabilities),
            Model = request.Model,
            Connection = connection.Environment,
            Processes = tree,
        };
        var resumed = capabilities.Has<Resumable>()
            ? await request.Resume.Match(
                token => ResumeAsync(provider, fresh with { Resume = token }, cancellationToken),
                () => Task.FromResult(Option<IAgentSession>.None))
            : Option<IAgentSession>.None;

        var started = await resumed.Match(
            session => Task.FromResult(Result<StartedSession, AgentError>.Success(new StartedSession(provider.Info, capabilities, session, connection.Name, tree.Id, Resumed: true))),
            async () => (await provider.StartAsync(fresh, cancellationToken))
                .Map(session => new StartedSession(provider.Info, capabilities, session, connection.Name, tree.Id, Resumed: false)));

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
        ConnectionError.UnofferedModel => AgentError.UnofferedModel,
        ConnectionError.UnofferedEffort => AgentError.UnofferedEffort,
        _ => AgentError.UnusableConnection,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot open a session on {Connection}: the model choice is refused as {Refusal}")]
    private partial void LogUnoffered(string connection, ModelRefusal refusal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot open a session on {Connection}: {Error}")]
    private partial void LogUnavailable(string connection, ConnectionError error);
}

internal sealed class ProviderChain(IEnumerable<HarnessTool> tools, IEnumerable<IAgentProviderDecorator> decorators)
{
    public IAgentProvider Decorate(IAgentProvider provider) =>
        decorators.Aggregate(provider, (inner, decorator) => decorator.Decorate(inner));

    public IReadOnlyList<HarnessTool> ToolsFor(CapabilitySet capabilities) =>
        capabilities.Get<AcceptsTools>().Match<IReadOnlyList<HarnessTool>>(accepted => [.. tools.Where(tool => accepted.Accepts(tool.Surface))], () => []);
}

internal sealed record StartedSession(
    ProviderInfo Provider,
    CapabilitySet Capabilities,
    IAgentSession Session,
    ConnectionName Connection,
    ProcessTreeId Tree,
    bool Resumed);
