using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.TurnChecks;

internal sealed class EvaluateTurn(JobLedger ledger, IWorkspaces workspaces, CompletionGates gates, IAgents agents)
{
    private const string DefaultFeedback = "The checks did not pass. Review the failures and fix them.";

    public async Task ExecuteAsync(Job job, SessionId session, TurnOutcome outcome, CancellationToken cancellationToken)
    {
        if (job.State != JobState.Running || job.Session != Option<SessionId>.Some(session))
        {
            return;
        }

        if (outcome != TurnOutcome.Finished)
        {
            await FailAsync(job, FailureReason.AgentFailed, cancellationToken);
            return;
        }

        _ = job.CompleteTurn();
        await ledger.RecordAsync(job, cancellationToken);
        await JudgeAsync(job, RetryInSameSessionAsync, cancellationToken);
    }

    public async Task JudgeAsync(Job job, Func<Job, Feedback, CancellationToken, Task> retry, CancellationToken cancellationToken) =>
        await (await EvaluateAsync(job, cancellationToken)).Match(
            verdict => verdict.Decision == GateDecision.Pass
                ? PassAsync(job, cancellationToken)
                : RetryAsync(job, verdict.Feedback, retry, cancellationToken),
            () => FailAsync(job, FailureReason.WorkspaceUnavailable, cancellationToken));

    private async Task<Option<GateVerdict>> EvaluateAsync(Job job, CancellationToken cancellationToken)
    {
        var attempt = job.Attempts[^1].Number.Value;

        if ((await workspaces.CheckpointAsync(job, $"Attempt {attempt}", cancellationToken)).IsFailure
            || !(await workspaces.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return Option<GateVerdict>.None;
        }

        return await gates.EvaluateAsync(new CompletedAttempt(job.Id, attempt, workspace.Path, job.Instruction.Text), cancellationToken);
    }

    private async Task PassAsync(Job job, CancellationToken cancellationToken)
    {
        _ = job.Pass();
        await ledger.RecordAsync(job, cancellationToken);
    }

    private async Task RetryAsync(Job job, string reason, Func<Job, Feedback, CancellationToken, Task> retry, CancellationToken cancellationToken)
    {
        if (!Feedback.Create(string.IsNullOrWhiteSpace(reason) ? DefaultFeedback : reason).TryGetValue(out var feedback, out _))
        {
            return;
        }

        if (job.RequestHelp().IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
            return;
        }

        await retry(job, feedback, cancellationToken);
    }

    private async Task RetryInSameSessionAsync(Job job, Feedback feedback, CancellationToken cancellationToken)
    {
        if (job.Retry(feedback).IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
            _ = await agents.TellAsync(job, feedback.Text, cancellationToken);
        }
    }

    private async Task FailAsync(Job job, FailureReason reason, CancellationToken cancellationToken)
    {
        _ = job.Fail(reason);
        await ledger.RecordAsync(job, cancellationToken);
    }
}
