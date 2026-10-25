using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Fleet.Connecting;
using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Verification.Contracts;

namespace Avala.Fleet.Recording;

internal sealed class FleetRecorder(Outbox outbox, IJobFacts jobs, FleetSession session, TimeProvider clock) :
    IHandle<JobSubmitted>,
    IHandle<JobProgressed>,
    IHandle<JobHeld>,
    IHandle<JobModelChosen>,
    IHandle<JobSessionStarted>,
    IHandle<AgentActivity>,
    IHandle<AttemptVerified>,
    IHandle<SessionEnded>
{
    private readonly Dictionary<SessionId, JobId> sessions = [];

    private readonly Dictionary<SessionId, string> models = [];

    private readonly Dictionary<JobId, bool> scratch = [];

    public const int HeldPerJob = 100;

    private readonly Dictionary<JobId, List<Fact>> held = [];

    private string heldFor = string.Empty;

    private Option<DateTimeOffset> last;

    public ValueTask HandleAsync(JobSubmitted integrationEvent, CancellationToken cancellationToken) =>
        RecordAsync(integrationEvent.Job, Option<IFactBody>.None, Option<DateTimeOffset>.None, cancellationToken);

    public ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken) =>
        RecordAsync(integrationEvent.Job, new JobProgressedFact(integrationEvent.Job.Value, Wire.Name(integrationEvent.Status)), Option<DateTimeOffset>.None, cancellationToken);

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            integrationEvent.Hold.Job,
            new JobHeldFact(integrationEvent.Hold.Job.Value, Wire.Name(integrationEvent.Hold.Reason)),
            Option<DateTimeOffset>.None,
            cancellationToken);

    public ValueTask HandleAsync(JobModelChosen integrationEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            integrationEvent.Job,
            new JobModelChosenFact(integrationEvent.Job.Value, FactRules.Model(integrationEvent.Model.Model), FactRules.Effort(integrationEvent.Model.Effort)),
            Option<DateTimeOffset>.None,
            cancellationToken);

    public ValueTask HandleAsync(AttemptVerified integrationEvent, CancellationToken cancellationToken) =>
        RecordAsync(
            integrationEvent.Report.Job,
            new AttemptVerifiedFact(integrationEvent.Report.Job.Value, integrationEvent.Report.Attempt, Wire.Name(integrationEvent.Report.Outcome)),
            integrationEvent.Report.VerifiedAt,
            cancellationToken);

    public ValueTask HandleAsync(JobSessionStarted integrationEvent, CancellationToken cancellationToken)
    {
        sessions[integrationEvent.Session] = integrationEvent.Job;

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(SessionEnded integrationEvent, CancellationToken cancellationToken)
    {
        sessions.Remove(integrationEvent.Session);
        models.Remove(integrationEvent.Session);

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(AgentActivity integrationEvent, CancellationToken cancellationToken)
    {
        switch (integrationEvent.Event)
        {
            case ModelReported reported:
                models[reported.Session] = reported.Model;
                break;
            case UsageReported usage:
                return RecordUsageAsync(usage, cancellationToken);
        }

        return ValueTask.CompletedTask;
    }

    private ValueTask RecordUsageAsync(UsageReported usage, CancellationToken cancellationToken)
    {
        var job = sessions.TryGetValue(usage.Session, out var found) ? found : Option<JobId>.None;
        var tokens = FactRules.Tokens(usage.Tokens.Input, usage.Tokens.Output, usage.Tokens.CacheRead, usage.Tokens.CacheWrite, usage.Tokens.Reasoning);

        return tokens.Match(
            counts => RecordAsync(
                job,
                new UsageReportedFact(
                    job.Map(owner => owner.Value),
                    counts,
                    usage.Cost.Bind(cost => FactRules.Cost(cost.Amount, cost.Currency, Wire.Name(cost.Basis))),
                    FactRules.Model(models.TryGetValue(usage.Session, out var model) ? model : Option<string>.None)),
                Option<DateTimeOffset>.None,
                cancellationToken),
            _ => ValueTask.CompletedTask);
    }

    private ValueTask RecordAsync(JobId job, Option<IFactBody> body, Option<DateTimeOffset> at, CancellationToken cancellationToken) =>
        RecordAsync(Option<JobId>.Some(job), body, at, cancellationToken);

    private async ValueTask RecordAsync(Option<JobId> job, Option<IFactBody> body, Option<DateTimeOffset> at, CancellationToken cancellationToken)
    {
        if (!session.Records || !session.Scope.Match(found => true, () => false))
        {
            held.Clear();

            return;
        }

        var scope = session.Scope.Match(found => found.Id, () => string.Empty);

        if (heldFor != scope)
        {
            held.Clear();
            heldFor = scope;
        }

        await job.Match(
            owner => RecordOfJobAsync(scope, owner, body, at, cancellationToken),
            () => AppendAsync(scope, body, at, cancellationToken));
    }

    private async Task RecordOfJobAsync(string scope, JobId job, Option<IFactBody> body, Option<DateTimeOffset> at, CancellationToken cancellationToken)
    {
        var verdict = await VerdictAsync(job, cancellationToken);

        if (verdict.IsNone)
        {
            await body.Match(
                async fact =>
                {
                    var facts = Held(job);

                    if (facts.Count < HeldPerJob)
                    {
                        facts.Add(new Fact(Guid.CreateVersion7(), await StampAsync(at, cancellationToken), fact));
                    }
                },
                () => Task.CompletedTask);

            return;
        }

        var waiting = held.Remove(job, out var facts) ? facts : [];

        if (verdict.Match(isScratch => isScratch, () => true))
        {
            return;
        }

        await AnnounceAsync(scope, job, cancellationToken);

        foreach (var fact in waiting)
        {
            await outbox.AppendAsync(scope, fact, cancellationToken);
        }

        await AppendAsync(scope, body, at, cancellationToken);
    }

    private async Task AppendAsync(string scope, Option<IFactBody> body, Option<DateTimeOffset> at, CancellationToken cancellationToken) =>
        await body.Match(
            async fact => await outbox.AppendAsync(scope, new Fact(Guid.CreateVersion7(), await StampAsync(at, cancellationToken), fact), cancellationToken),
            () => Task.CompletedTask);

    private List<Fact> Held(JobId job)
    {
        if (!held.TryGetValue(job, out var facts))
        {
            facts = [];
            held[job] = facts;
        }

        return facts;
    }

    private async Task<Option<bool>> VerdictAsync(JobId job, CancellationToken cancellationToken)
    {
        if (scratch.TryGetValue(job, out var known))
        {
            return known;
        }

        var found = await jobs.IsScratchAsync(job, cancellationToken);

        return found.Match(
            verdict =>
            {
                scratch[job] = verdict;

                return Option<bool>.Some(verdict);
            },
            () => Option<bool>.None);
    }

    private async Task AnnounceAsync(string scope, JobId job, CancellationToken cancellationToken)
    {
        if (await outbox.RunAsync((store, token) => store.IsAnnouncedAsync(scope, job.Value, token), cancellationToken))
        {
            return;
        }

        var announcement = await jobs.AnnouncementAsync(job, cancellationToken);

        await announcement.Match(
            async found =>
            {
                await found.Repository.Match(
                    repository => outbox.AppendAsync(
                        scope,
                        new Fact(
                            Guid.CreateVersion7(),
                            found.Submitted.ToUniversalTime(),
                            new JobSubmittedFact(job.Value, repository, found.Parent, FactRules.Model(found.Model), FactRules.Effort(found.Effort))),
                        cancellationToken),
                    () => Task.CompletedTask);
                await outbox.RunAsync(
                    async (store, token) =>
                    {
                        await store.AnnounceAsync(scope, job.Value, found.Repository.IsNone, token);

                        return true;
                    },
                    cancellationToken);
            },
            () => Task.CompletedTask);
    }

    private async Task<DateTimeOffset> StampAsync(Option<DateTimeOffset> at, CancellationToken cancellationToken)
    {
        var previous = await last.Match(
            known => Task.FromResult(known),
            () => outbox.RunAsync((store, token) => store.LatestStampAsync(token), cancellationToken));
        var stamp = Stamps.Next(at.Match(given => given, clock.GetUtcNow), previous);
        last = stamp;

        return stamp;
    }
}

internal static class Wire
{
    public static string Name<T>(T value)
        where T : struct, Enum
    {
        var name = value.ToString();

        return string.Concat(char.ToLowerInvariant(name[0]).ToString(), name[1..]);
    }
}
