using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Briefing;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Watching;

internal sealed class PersonHandoff(Situations situations, BriefGatherer briefs, IJobs jobs) : IHandoffRequests
{
    public async ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, ConnectionName to, CancellationToken cancellationToken) =>
        await (await situations.OfAsync(job, cancellationToken)).Match(
            situation => HandOffAsync(situation, to, cancellationToken),
            () => Task.FromResult(Result<JobContinuation, JobRejection>.Failure(JobRejection.UnknownJob)));

    private async Task<Result<JobContinuation, JobRejection>> HandOffAsync(JobSituation situation, ConnectionName to, CancellationToken cancellationToken)
    {
        if (situation.Connection == to)
        {
            return JobRejection.SameConnection;
        }

        if (situation.Situation.Usable.FirstOrDefault(usable => usable.Connection == to) is not { } destination)
        {
            return JobRejection.UnusableConnection;
        }

        var now = situation.Situation.Now;
        var choice = new ConnectionChoice(to, ChoiceReason.Person, [Capacity.Measure(to, destination.Limits, situation.Situation.Rules.Threshold, now)], now);
        var brief = await briefs.WriteAsync(new BriefRequest(situation.History, situation.Connection, to, Option<LimitReason>.None, Option<string>.None), cancellationToken);

        return await jobs.HandOffAsync(situation.Job, new JobHandoff(choice, brief), cancellationToken);
    }
}
