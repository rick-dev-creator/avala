using System.Collections.Immutable;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Records;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Escalating;

internal sealed record UntoldQuestion(JobId Parent, JobId Child, ParentAsked Asked);

internal sealed class UntoldQuestions(DelegationJournal journal) : IJobBriefing
{
    private ImmutableDictionary<(SessionId Session, ItemId Item), UntoldQuestion> untold = ImmutableDictionary<(SessionId Session, ItemId Item), UntoldQuestion>.Empty;

    public void Keep(UntoldQuestion question) =>
        ImmutableInterlocked.AddOrUpdate(ref untold, (question.Asked.Session, question.Asked.Item), question, (_, _) => question);

    public void Forget(SessionId session, ItemId item) => ImmutableInterlocked.TryRemove(ref untold, (session, item), out _);

    public IReadOnlyList<UntoldQuestion> Claim(Func<UntoldQuestion, bool> which)
    {
        var claimed = new List<UntoldQuestion>();

        foreach (var key in Volatile.Read(ref untold).Where(pending => which(pending.Value)).Select(pending => pending.Key))
        {
            if (ImmutableInterlocked.TryRemove(ref untold, key, out var question))
            {
                claimed.Add(question);
            }
        }

        return claimed;
    }

    public void Release(IReadOnlyList<UntoldQuestion> waiting)
    {
        foreach (var question in waiting)
        {
            ImmutableInterlocked.TryAdd(ref untold, (question.Asked.Session, question.Asked.Item), question);
        }
    }

    public async Task ToldAsync(IReadOnlyList<UntoldQuestion> waiting, bool woken, CancellationToken cancellationToken)
    {
        foreach (var question in waiting)
        {
            await journal.AskedAsync(question.Asked with { Woken = woken }, cancellationToken);
        }
    }

    public static string Folded(IReadOnlyList<UntoldQuestion> waiting) => string.Join("\n\n", waiting.Select(question => question.Asked.Note));

    public ValueTask<Option<JobBrief>> BriefAsync(JobId job, CancellationToken cancellationToken)
    {
        var waiting = Claim(question => question.Parent == job);

        return ValueTask.FromResult(waiting.Count == 0
            ? Option<JobBrief>.None
            : new JobBrief(Folded(waiting))
            {
                Delivered = token => ToldAsync(waiting, woken: false, token),
                Undelivered = _ =>
                {
                    Release(waiting);

                    return Task.CompletedTask;
                },
            });
    }
}
