using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Domain;
using Avala.Server.Activity.Infrastructure.Storage;

namespace Avala.Server.Activity.Infrastructure.Ingestion;

internal sealed class EventApplication(Guid instanceId, Dictionary<Guid, FleetJob> jobs, FleetInstance instance)
{
    private readonly List<FleetJob> started = [];

    public IReadOnlyList<FleetJob> Started => started;

    public static Guid? JobOf(IIncomingEvent incoming) => incoming switch
    {
        JobSubmittedEvent submitted => submitted.Job,
        JobModelChosenEvent chosen => chosen.Job,
        JobProgressedEvent progressed => progressed.Job,
        JobHeldEvent held => held.Job,
        UsageReportedEvent usage => usage.Job,
        AttemptVerifiedEvent verified => verified.Job,
        _ => null,
    };

    public IReadOnlyList<ReceivedEvent> ApplyAll(IngestionBatch batch, DateTimeOffset receivedAt)
    {
        List<ReceivedEvent> received = [];

        foreach (var (incoming, at) in batch.InOrder)
        {
            if (Apply(incoming, at) is { } refusal)
            {
                batch.Reject(incoming.Id, refusal);
                continue;
            }

            batch.Accept(incoming.Id);
            received.Add(new ReceivedEvent(instanceId, incoming.Id, receivedAt));
        }

        return received;
    }

    public string? Apply(IIncomingEvent incoming, EventTime at) => incoming switch
    {
        JobSubmittedEvent submitted => Submit(submitted, at),
        JobModelChosenEvent chosen => Choose(chosen, at),
        JobProgressedEvent progressed => Change(progressed.Job, at, job => job.Progress(progressed.Status.ToDomain(), at)),
        JobHeldEvent held => Change(held.Job, at, job => job.HoldFor(held.Reason.ToDomain(), at)),
        UsageReportedEvent usage => Spend(usage, at),
        AttemptVerifiedEvent verified => Verify(verified, at),
        _ => "the event's kind is unknown",
    };

    private string? Submit(JobSubmittedEvent submitted, EventTime at)
    {
        var repository = RepositoryKey.Create(submitted.Repository);
        var choice = ModelChoice.Create(submitted.Model, submitted.Effort);
        jobs.TryGetValue(submitted.Job, out var known);

        if ((repository.Refusal ?? choice.Refusal ?? FleetJob.RefusesSubmission(known, repository.Value)) is { } refusal)
        {
            return refusal;
        }

        return Change(submitted.Job, at, job => job.Submit(repository.Value, submitted.Parent, choice.Value, at));
    }

    private string? Choose(JobModelChosenEvent chosen, EventTime at)
    {
        var choice = ModelChoice.Create(chosen.Model, chosen.Effort);

        return choice.Refusal ?? Change(chosen.Job, at, job => job.ChooseModel(choice.Value, at));
    }

    private string? Spend(UsageReportedEvent usage, EventTime at)
    {
        var tokens = TokenCounts.Create(usage.InputTokens, usage.OutputTokens, usage.CacheReadTokens, usage.CacheWriteTokens, usage.ReasoningTokens);
        var cost = usage.Cost is { } incoming ? CostAmount.Create(incoming.Amount, incoming.Currency, incoming.Basis.ToDomain()) : null;
        var model = ModelChoice.Create(usage.Model, effort: null);

        if ((tokens.Refusal ?? cost?.Refusal ?? model.Refusal) is { } refusal)
        {
            return refusal;
        }

        if (usage.Job is { } job)
        {
            return Change(job, at, found => found.Spend(tokens.Value, cost?.Value, at));
        }

        instance.Spend(tokens.Value, cost?.Value);

        return null;
    }

    private string? Verify(AttemptVerifiedEvent verified, EventTime at)
    {
        var attempt = Attempt.Create(verified.Attempt, verified.Outcome.ToDomain());

        return attempt.Refusal ?? Change(verified.Job, at, job => job.Verify(attempt.Value, at));
    }

    private string? Change(Guid id, EventTime at, Action<FleetJob> change)
    {
        if (!jobs.TryGetValue(id, out var job))
        {
            job = FleetJob.Start(instanceId, id, at);
            jobs[id] = job;
            started.Add(job);
        }

        change(job);

        return null;
    }
}
