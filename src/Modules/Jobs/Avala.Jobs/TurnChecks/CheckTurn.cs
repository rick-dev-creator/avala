using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Launching;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.TurnChecks;

internal sealed class CheckTurn(JobLedger ledger, IWorkspaces workspaces, CompletionGates gates, IAgents agents) : IHandle<TurnFinished>
{
    private const string DefaultFeedback = "The checks did not pass. Review the failures and fix them.";

    public async ValueTask HandleAsync(TurnFinished integrationEvent, CancellationToken cancellationToken) =>
        await ledger.FindBySessionAsync(integrationEvent.Session, cancellationToken).MatchAsync(
            job => job.State == JobState.Running ? CheckAsync(job, integrationEvent.Outcome, cancellationToken) : Task.CompletedTask,
            () => Task.CompletedTask);

    private async Task CheckAsync(Job job, TurnOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome != TurnOutcome.Finished)
        {
            await FailAsync(job, FailureReason.AgentFailed, cancellationToken);
            return;
        }

        _ = job.CompleteTurn();
        await ledger.RecordAsync(job, cancellationToken);

        await (await EvaluateAsync(job, cancellationToken)).Match(
            verdict => verdict.Decision == GateDecision.Pass
                ? PassAsync(job, cancellationToken)
                : RetryAsync(job, verdict.Feedback, cancellationToken),
            () => FailAsync(job, FailureReason.WorkspaceUnavailable, cancellationToken));
    }

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

    private async Task RetryAsync(Job job, string reason, CancellationToken cancellationToken)
    {
        if (!Feedback.Create(string.IsNullOrWhiteSpace(reason) ? DefaultFeedback : reason).TryGetValue(out var feedback, out _))
        {
            return;
        }

        if (job.Retry(feedback).IsFailure)
        {
            _ = job.RequestHelp();
            await ledger.RecordAsync(job, cancellationToken);
            return;
        }

        await ledger.RecordAsync(job, cancellationToken);
        _ = await agents.TellAsync(job, feedback.Text, cancellationToken);
    }

    private async Task FailAsync(Job job, FailureReason reason, CancellationToken cancellationToken)
    {
        _ = job.Fail(reason);
        await ledger.RecordAsync(job, cancellationToken);
    }
}
