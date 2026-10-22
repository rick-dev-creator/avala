using System.Collections.Immutable;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Contracts.Presentation;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

internal sealed class MessageLedger : IPlugin, IAgentProviderDecorator
{
    private ImmutableDictionary<MessageId, (JobId Job, bool Accepted, int Attempt)> sent = ImmutableDictionary<MessageId, (JobId, bool, int)>.Empty;
    private ImmutableDictionary<MessageId, (JobId Job, QueueChange Change)> queue = ImmutableDictionary<MessageId, (JobId, QueueChange)>.Empty;
    private ImmutableDictionary<MessageId, int> reached = ImmutableDictionary<MessageId, int>.Empty;

    public PluginInfo Info { get; } = new("message-ledger", "Message ledger");

    public IReadOnlyList<SentMessage> Messages
    {
        get
        {
            var (delivered, accepted, queued) = (Volatile.Read(ref reached), Volatile.Read(ref sent), Volatile.Read(ref queue));

            return [.. accepted.Keys.Union(queued.Keys).Select(id => accepted.TryGetValue(id, out var known)
                ? new SentMessage(known.Job, id, known.Accepted, delivered.GetValueOrDefault(id)) { Attempt = known.Attempt, Queue = queued.TryGetValue(id, out var change) ? change.Change : Option<QueueChange>.None }
                : new SentMessage(queued[id].Job, id, false, delivered.GetValueOrDefault(id)) { Queue = queued[id].Change })];
        }
    }

    public void Register(IPluginRegistrar registrar)
    {
        registrar.Services.AddSingleton(this);
        registrar.Services.AddSingleton<IAgentProviderDecorator>(this);
        Decorate<IJobMessages>(registrar.Services, (provider, inner) =>
        {
            provider.GetRequiredService<IMessenger>().Register<MessageLedger, QueuedMessageChanged>(this, static (ledger, changed) => ledger.Changed(changed));

            return new WatchedMessages(inner, provider.GetRequiredService<IJobCatalog>(), this);
        });
        Decorate<IJobs>(registrar.Services, (provider, inner) => new WatchedJobs(inner, provider.GetRequiredService<IJobMessages>(), provider.GetRequiredService<IJobCatalog>(), this));
    }

    public void Sent<T>(JobId job, JobMessage message, Result<T, JobRejection> outcome, int attempt)
        where T : notnull
    {
        foreach (var id in message.Messages)
        {
            _ = ImmutableInterlocked.Update(ref sent, known => known.SetItem(id, (job, outcome.IsSuccess, attempt)));
        }
    }

    public void Changed(QueuedMessageChanged changed)
    {
        foreach (var id in changed.Messages)
        {
            _ = ImmutableInterlocked.Update(ref queue, known => known.SetItem(id, (changed.Job, changed.Change)));
        }
    }

    private async Task<Result<T, JobRejection>> WatchedAsync<T>(JobId job, JobMessage message, Func<ValueTask<Result<T, JobRejection>>> send, IJobCatalog catalog)
        where T : notnull
    {
        var history = await catalog.HistoryAsync(job, CancellationToken.None);
        var outcome = await send();
        Sent(job, message, outcome, history.Match(found => found.Attempts is [.., var last] ? last.Number : 0, () => 0));

        return outcome;
    }

    public IAgentProvider Decorate(IAgentProvider provider) => new WatchedProvider(provider, this);

    public void Said(UserTurn turn, Result<TurnId, AgentError> outcome)
    {
        foreach (var id in outcome.IsSuccess ? turn.Messages : [])
        {
            _ = ImmutableInterlocked.AddOrUpdate(ref reached, id, 1, (_, count) => count + 1);
        }
    }

    private static void Decorate<TService>(IServiceCollection services, Func<IServiceProvider, TService, TService> watch)
        where TService : class
    {
        var original = services.Last(descriptor => descriptor.ServiceType == typeof(TService));
        services.Remove(original);
        services.AddSingleton(provider => watch(provider, (TService)original.ImplementationFactory!(provider)));
    }

    private sealed class WatchedMessages(IJobMessages inner, IJobCatalog catalog, MessageLedger ledger) : IJobMessages
    {
        public async ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
            await ledger.WatchedAsync(job, message, () => inner.ContinueAsync(job, message, cancellationToken), catalog);

        public async ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
            await ledger.WatchedAsync(job, message, () => inner.SteerAsync(job, message, cancellationToken), catalog);

        public async ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, JobMessage message, CancellationToken cancellationToken) =>
            await ledger.WatchedAsync(job, message, () => inner.SendBackAsync(job, message, cancellationToken), catalog);
    }

    private sealed class WatchedJobs(IJobs inner, IJobMessages messages, IJobCatalog catalog, MessageLedger ledger) : IJobs
    {
        public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) => inner.SubmitAsync(request, cancellationToken);

        public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => inner.HoldAsync(job, reason, cancellationToken);

        public ValueTask<Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => inner.HoldRunningAsync(job, reason, cancellationToken);

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) => messages.ContinueAsync(job, new JobMessage(message), cancellationToken);

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, JobMessage message, CancellationToken cancellationToken) => messages.ContinueAsync(job, message, cancellationToken);

        public async ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, JobMessage message, CancellationToken cancellationToken) =>
            await ledger.WatchedAsync(job, message, () => inner.ContinueOnAsync(job, connection, message, cancellationToken), catalog);

        public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, JobMessage message, CancellationToken cancellationToken) => messages.SteerAsync(job, message, cancellationToken);

        public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) => inner.DiscardAsync(job, cancellationToken);

        public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => inner.ApproveAsync(job, cancellationToken);

        public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => messages.SendBackAsync(job, new JobMessage(feedback), cancellationToken);

        public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => inner.ResumeAsync(job, cancellationToken);

        public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) => inner.HandOffAsync(job, handoff, cancellationToken);

        public ValueTask<Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, ModelChoice model, CancellationToken cancellationToken) => inner.ChooseModelAsync(job, model, cancellationToken);
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

internal sealed record SentMessage(JobId Job, MessageId Message, bool Accepted, int Reached)
{
    public int Attempt { get; init; }

    public Option<QueueChange> Queue { get; init; }
}
