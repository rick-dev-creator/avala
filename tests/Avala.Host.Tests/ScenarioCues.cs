using System.Collections.Immutable;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class ScenarioCues : IPlugin, IAgentProviderDecorator
{
    private ImmutableDictionary<string, string> cues = ImmutableDictionary<string, string>.Empty;

    public PluginInfo Info { get; } = new("scenario-cues", "Scenario cues");

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddSingleton<IAgentProviderDecorator>(this);

    public void Cue(string instruction, string scenario) => ImmutableInterlocked.AddOrUpdate(ref cues, instruction, scenario, (_, _) => scenario);

    public IAgentProvider Decorate(IAgentProvider provider) => new CuedProvider(provider, this);

    private UserTurn Told(UserTurn turn) =>
        Volatile.Read(ref cues).FirstOrDefault(cue => turn.Text.StartsWith(cue.Key, StringComparison.Ordinal)) is { Key: not null } found
            ? turn with { Text = $"[simulate: {found.Value}] {turn.Text}" }
            : turn;

    private sealed class CuedProvider(IAgentProvider inner, ScenarioCues cues) : IAgentProvider
    {
        public ProviderInfo Info => inner.Info;

        public CapabilitySet CapabilitiesOn(ConnectionEnvironment connection) => inner.CapabilitiesOn(connection);

        public async ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken) =>
            (await inner.StartAsync(options, cancellationToken)).Map<IAgentSession>(session => new CuedSession(session, cues));
    }

    private sealed class CuedSession(IAgentSession inner, ScenarioCues cues) : IAgentSession
    {
        public SessionId Id => inner.Id;

        public Option<AgentAccount> Account => inner.Account;

        public IAsyncEnumerable<IAgentEvent> Events => inner.Events;

        public ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) => inner.SendAsync(cues.Told(turn), cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) => inner.RespondAsync(decision, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken) => inner.AnswerAsync(answer, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken) => inner.ReturnAsync(result, cancellationToken);

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) => inner.InterruptAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
