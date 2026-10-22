using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Workbench.Timeline;

internal static class RetryLines
{
    public static Transcript Apply(Transcript transcript, IAgentEvent activity) => activity switch
    {
        HarnessRetrying retrying => transcript.Put(new RetryEntry(EntryKeys.Retry(retrying.Turn), retrying.Attempt, retrying.Most, retrying.Wait, retrying.Reason)),
        TurnCompleted { Outcome: not TurnOutcome.Finished } failed => transcript.Change(EntryKeys.Retry(failed.Turn), entry => entry is RetryEntry { Recovered: false } retry ? retry with { GaveUp = true } : entry),
        _ => transcript,
    };

    public static Transcript Recovered(Transcript transcript, TurnId turn) =>
        transcript.Change(EntryKeys.Retry(turn), entry => entry is RetryEntry { Recovered: false, GaveUp: false } retry ? retry with { Recovered = true } : entry);
}
