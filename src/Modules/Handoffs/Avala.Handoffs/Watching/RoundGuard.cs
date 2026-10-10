using Avala.Handoffs.Briefing;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;

namespace Avala.Handoffs.Watching;

internal sealed class RoundGuard(Situations situations, BriefGatherer briefs, LimitWatch watch) : IRoundRouter
{
    public async ValueTask<RoundRoute> RouteAsync(RoundQuestion question, CancellationToken cancellationToken) =>
        await (await situations.OfAsync(question.Job, cancellationToken)).Match(
            situation => RouteAsync(situation, question, cancellationToken),
            () => Task.FromResult(RoundRoute.GoOn));

    private async Task<RoundRoute> RouteAsync(JobSituation situation, RoundQuestion question, CancellationToken cancellationToken)
    {
        var decision = HandoffPolicy.ByThreshold(situation.Situation);

        if (decision.Verdict == Verdict.GoOn)
        {
            return RoundRoute.GoOn;
        }

        watch.Pending(question.Job, question.Feedback);

        return await decision.Choice.Match(
            async choice => RoundRoute.To(new JobHandoff(
                choice,
                await briefs.WriteAsync(new BriefRequest(situation.History, situation.Connection, choice.Connection, decision.Why, question.Feedback), cancellationToken))),
            () => Task.FromResult(RoundRoute.Held(HoldReason.LimitNearlyReached)));
    }
}
