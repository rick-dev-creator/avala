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

internal sealed class EvaluateTurn(JobLedger ledger, IWorkspaces workspaces, CompletionGates gates, NextRound round)
{
    private const string DefaultFeedback = "The checks did not pass. Review the failures and fix them.";

    public async Task ExecuteAsync(Job job, TurnFinished finished, CancellationToken cancellationToken)
    {
        if (job.State != JobState.Running || job.Session != Option<SessionId>.Some(finished.Session))
        {
            return;
        }

        switch (finished.Outcome)
        {
            case TurnOutcome.Finished:
                _ = job.CompleteTurn();
                await ledger.RecordAsync(job, cancellationToken);
                await JudgeAsync(job, new FinishedTurn(finished.Session, finished.Turn), round.InSameSessionAsync, cancellationToken);
                break;
            case TurnOutcome.LimitReached:
                await round.HoldAsync(job, HoldReason.LimitNearlyReached, finished.Limit, cancellationToken);
                break;
            case var _ when job.Attempts[^1].Origin == AttemptOrigin.Woken || job.Parent.IsSome:
                await round.HoldAsync(job, HoldReason.TurnFailed, Option<UsageLimit>.None, cancellationToken);
                break;
            default:
                await FailAsync(job, JobFailure.AgentFailed, cancellationToken);
                break;
        }
    }

    public Task JudgeAsync(Job job, Func<Job, Feedback, CancellationToken, Task> retry, CancellationToken cancellationToken) =>
        JudgeAsync(job, Option<FinishedTurn>.None, retry, cancellationToken);

    private async Task JudgeAsync(Job job, Option<FinishedTurn> turn, Func<Job, Feedback, CancellationToken, Task> retry, CancellationToken cancellationToken) =>
        await (await EvaluateAsync(job, turn, cancellationToken)).Match(
            verdict => verdict.Hold.Match(
                reason => round.HoldAsync(job, reason, cancellationToken),
                () => verdict.Decision == GateDecision.Pass
                    ? PassAsync(job, cancellationToken)
                    : RetryAsync(job, verdict.Feedback, retry, cancellationToken)),
            () => FailAsync(job, JobFailure.WorkspaceUnavailable, cancellationToken));

    private async Task<Option<GateVerdict>> EvaluateAsync(Job job, Option<FinishedTurn> turn, CancellationToken cancellationToken)
    {
        var attempt = job.Attempts[^1].Number.Value;

        if ((await workspaces.CheckpointAsync(job, $"{Review.JobCheckpointDesk.AttemptLabel}{attempt}", cancellationToken)).IsFailure
            || !(await workspaces.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return Option<GateVerdict>.None;
        }

        return await gates.EvaluateAsync(new CompletedAttempt(job.Id, attempt, workspace.Path, job.Instruction.Text) { Turn = turn }, cancellationToken);
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

        await round.GoOnAsync(job, feedback, retry, cancellationToken);
    }

    private async Task FailAsync(Job job, JobFailure reason, CancellationToken cancellationToken)
    {
        _ = job.Fail(reason, ledger.Now);
        await ledger.RecordAsync(job, cancellationToken);
    }
}
