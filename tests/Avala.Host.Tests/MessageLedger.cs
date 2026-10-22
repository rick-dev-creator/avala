using System.Collections.Immutable;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class MessageLedger : IPlugin, IAgentProviderDecorator
{
    private ImmutableDictionary<MessageId, (JobId Job, bool Accepted)> sent = ImmutableDictionary<MessageId, (JobId, bool)>.Empty;
    private ImmutableDictionary<MessageId, int> reached = ImmutableDictionary<MessageId, int>.Empty;

    public PluginInfo Info { get; } = new("message-ledger", "Message ledger");

    public IReadOnlyList<SentMessage> Messages
    {
        get
        {
            var delivered = Volatile.Read(ref reached);

            return [.. Volatile.Read(ref sent).Select(message => new SentMessage(message.Value.Job, message.Key, message.Value.Accepted, delivered.GetValueOrDefault(message.Key)))];
        }
    }

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.AddSingleton(this);
        registrar.Services.AddSingleton<IAgentProviderDecorator>(this);
        Decorate<IJobMessages>(registrar.Services, inner => new WatchedMessages(inner, this));
    }

    public void Sent<T>(JobId job, JobMessage message, Result<T, JobRejection> outcome)
        where T : notnull
    {
        foreach (var id in message.Messages)
        {
            _ = ImmutableInterlocked.Update(ref sent, known => known.SetItem(id, (job, outcome.IsSuccess)));
        }
    }

    public IAgentProvider Decorate(IAgentProvider provider) => new WatchedProvider(provider, this);

    public void Said(UserTurn turn, Result<TurnId, AgentError> outcome)
    {
        foreach (var id in outcome.IsSuccess ? turn.Messages : [])
        {
            _ = ImmutableInterlocked.AddOrUpdate(ref reached, id, 1, (_, count) => count + 1);
        }
    }

    private static void Decorate<TService>(IServiceCollection services, Func<TService, TService> watch)
        where TService : class
    {
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(TService));
        services.Remove(original);
        services.AddSingleton(provider => watch((TService)original.ImplementationFactory!(provider)));
    }

    private sealed class WatchedMessages(IJobMessages inner, MessageLedger ledger) : IJobMessages
    {
        public async ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
            Watched(job, message, await inner.ContinueAsync(job, message, cancellationToken));

        public async ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
            Watched(job, message, await inner.SteerAsync(job, message, cancellationToken));

        public async ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
            Watched(job, message, await inner.SendBackAsync(job, message, cancellationToken));

        private Result<T, JobRejection> Watched<T>(JobId job, JobMessage message, Result<T, JobRejection> outcome)
            where T : notnull
        {
            ledger.Sent(job, message, outcome);

            return outcome;
        }
    }

    private sealed class WatchedProvider(IAgentProvider inner, MessageLedger ledger) : IAgentProvider
    {
        public ProviderInfo Info => inner.Info;

        public CapabilitySet CapabilitiesOn(ConnectionEnvironment connection) => inner.CapabilitiesOn(connection);

        public async ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken) =>
            (await inner.StartAsync(options, cancellationToken)).Map<IAgentSession>(session => new WatchedSession(session, ledger));
    }

    private sealed class WatchedSession(IAgentSession inner, MessageLedger ledger) : IAgentSession
    {
        public SessionId Id => inner.Id;

        public Option<AgentAccount> Account => inner.Account;

        public IReadOnlyList<string> OwnFolders => inner.OwnFolders;

        public IAsyncEnumerable<IAgentEvent> Events => inner.Events;

        public async ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken)
        {
            var said = await inner.SendAsync(turn, cancellationToken);
            ledger.Said(turn, said);

            return said;
        }

        public ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken) => inner.RespondAsync(decision, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken) => inner.AnswerAsync(answer, cancellationToken);

        public ValueTask<Result<ItemId, AgentError>> ReturnAsync(ToolResult result, CancellationToken cancellationToken) => inner.ReturnAsync(result, cancellationToken);

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken) => inner.InterruptAsync(cancellationToken);

        public ValueTask<Result<RemoteState, AgentError>> ControlRemotelyAsync(bool on, CancellationToken cancellationToken) => inner.ControlRemotelyAsync(on, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

internal sealed record SentMessage(JobId Job, MessageId Message, bool Accepted, int Reached);
