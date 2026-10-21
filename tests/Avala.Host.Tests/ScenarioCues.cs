using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class ScenarioCues : IPlugin, IAgentProviderDecorator
{
    private const string Tag = "[simulate:";

    private ImmutableDictionary<string, string> cues = ImmutableDictionary<string, string>.Empty;

    public PluginInfo Info { get; } = new("scenario-cues", "Scenario cues");

    public bool HidesDelegatedCues { get; set; }

    public void Register(IPluginRegistrar registrar) => registrar.Services.AddSingleton<IAgentProviderDecorator>(this);

    public void Cue(string instruction, string scenario) => ImmutableInterlocked.AddOrUpdate(ref cues, instruction, scenario, (_, _) => scenario);

    public IAgentProvider Decorate(IAgentProvider provider) => new CuedProvider(provider, this);

    private UserTurn Told(UserTurn turn) =>
        Volatile.Read(ref cues).FirstOrDefault(cue => turn.Text.StartsWith(cue.Key, StringComparison.Ordinal)) is { Key: not null } found
            ? turn with { Text = $"[simulate: {found.Value}] {turn.Text}" }
            : turn;

    private IAgentEvent Hidden(IAgentEvent agentEvent) =>
        HidesDelegatedCues && agentEvent is ToolCalled { Tool: "delegate" } called && JsonNode.Parse(called.Input) is JsonObject input && input["instruction"]?.GetValue<string>() is { } instruction && Tagged(instruction) is ({ } scenario, { } told)
            ? called with { Input = Untagged(input, scenario, told) }
            : agentEvent;

    private string Untagged(JsonObject input, string scenario, string told)
    {
        Cue(told, scenario);
        input["instruction"] = told;

        return input.ToJsonString();
    }

    private static (string? Scenario, string? Told) Tagged(string instruction) =>
        instruction.StartsWith(Tag, StringComparison.Ordinal) && instruction.IndexOf(']', StringComparison.Ordinal) is > 0 and var end
            ? (instruction[Tag.Length..end].Trim(), instruction[(end + 1)..].Trim())
            : (null, null);

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


        public IReadOnlyList<string> OwnFolders => inner.OwnFolders;

        public IAsyncEnumerable<IAgentEvent> Events => inner.Events.Select(cues.Hidden);

        public ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken) => inner.SendAsync(cues.Told(turn), cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) => inner.RespondAsync(decision, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken) => inner.AnswerAsync(answer, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken) => inner.ReturnAsync(result, cancellationToken);

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) => inner.InterruptAsync(cancellationToken);

        public ValueTask<Result<RemoteState, AgentError>> ControlRemotelyAsync(bool on, CancellationToken cancellationToken) => inner.ControlRemotelyAsync(on, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
