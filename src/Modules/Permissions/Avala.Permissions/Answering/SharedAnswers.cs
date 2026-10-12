using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal sealed class SharedAnswers(HumanAnswers answers, GovernanceBook book, AnswerLedger ledger, IEnumerable<IAnswerSharing> sharing) : IPermissionAnswers
{
    public async ValueTask<Result<HumanAnswer, PolicyError>> AnswerAsync(SessionId session, PermissionReply reply, CancellationToken cancellationToken)
    {
        var answered = await answers.AnswerAsync(session, reply, cancellationToken);

        if (reply.Remember is Remember.ForThisContest or Remember.EverythingInThisContest && answered.TryGetValue(out var given, out _))
        {
            foreach (var job in await SharersAsync(given.Job, cancellationToken))
            {
                await ShareAsync(job, given, reply, cancellationToken);
            }
        }

        return answered;
    }

    private async Task<IReadOnlyList<JobId>> SharersAsync(Option<JobId> job, CancellationToken cancellationToken)
    {
        var sharers = new List<JobId>();

        foreach (var shares in job.Match(found => sharing.Select(shared => (Sharing: shared, Job: found)), () => []))
        {
            sharers.AddRange(await shares.Sharing.SharersAsync(shares.Job, cancellationToken));
        }

        return [.. sharers.Distinct()];
    }

    private async Task ShareAsync(JobId job, HumanAnswer given, PermissionReply reply, CancellationToken cancellationToken)
    {
        var shared = false;
        var waiting = book.SessionsOf(job)
            .SelectMany(session => session.Decisions
                .Where(decision => decision.Delivery == DecisionDelivery.LeftToHuman && decision.Kind == given.Kind && decision.Target == given.Target)
                .Select(decision => (session.Session, decision.Item)))
            .ToList();

        foreach (var (session, item) in waiting)
        {
            shared |= (await answers.AnswerAsync(session, reply with { Item = item }, cancellationToken)).IsSuccess;
        }

        if (shared)
        {
            return;
        }

        foreach (var rule in given.Rule.Match(found => new[] { found }, () => []))
        {
            book.Remember(job, rule);
        }

        if (given.Everything && ledger.Raise(job))
        {
            await ledger.RaisedAsync(job, cancellationToken);
        }
    }
}
