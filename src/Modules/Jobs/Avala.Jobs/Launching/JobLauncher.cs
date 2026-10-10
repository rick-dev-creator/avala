using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Ledger;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Launching;

internal sealed class JobLauncher(JobLedger ledger, IAgents agents, WorkspacePlanner planner, JobMessenger messenger)
{
    public const string RestartNote = "The harness restarted while you were working on this job. Continue where you left off.";

    public async Task LaunchAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.State != JobState.Preparing)
        {
            return;
        }

        if (!(await planner.PrepareAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            await FailAsync(job, FailureReason.WorkspaceUnavailable, cancellationToken);
            return;
        }

        if (!(await planner.ConnectionOfAsync(job, workspace, cancellationToken)).TryGetValue(out var connection, out _))
        {
            await FailAsync(job, FailureReason.ConnectionUnavailable, cancellationToken);
            return;
        }

        if (!(await agents.OpenAsync(new AgentRequest(workspace.Path) { Connection = connection }, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            await FailAsync(job, Failure(error), cancellationToken);
            return;
        }

        if (job.Start(workspace.Id, opened.Session, opened.Connection).IsSuccess)
        {
            await BeginAsync(job, opened.Session, job.Instruction.Text, cancellationToken);
        }
    }

    public async Task RelaunchAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.State == JobState.Running)
        {
            await ReopenAsync(
                job,
                opened => job.Recover(opened.Session, opened.Resumed).IsSuccess,
                opened => opened.Resumed ? RestartNote : job.Instruction.Text,
                cancellationToken);
        }
    }

    public async Task RetryInNewSessionAsync(Job job, Feedback feedback, CancellationToken cancellationToken) =>
        await ReopenAsync(
            job,
            opened => job.Retry(feedback, opened.Session, opened.Resumed).IsSuccess,
            opened => opened.Resumed ? feedback.Text : $"{job.Instruction.Text}\n\n{feedback.Text}",
            cancellationToken);

    private async Task ReopenAsync(Job job, Func<OpenedSession, bool> begin, Func<OpenedSession, string> message, CancellationToken cancellationToken)
    {
        if (!(await planner.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            await FailAsync(job, FailureReason.WorkspaceUnavailable, cancellationToken);
            return;
        }

        if (!(await OpenAsync(job, workspace, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            await FailAsync(job, Failure(error), cancellationToken);
            return;
        }

        if (begin(opened))
        {
            await BeginAsync(job, opened.Session, message(opened), cancellationToken);
        }
    }

    public async Task<Result<JobContinuation, JobRejection>> ResumeAsync(Job job, CancellationToken cancellationToken)
    {
        if (job.Resume.IsNone || !(await planner.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return JobRejection.NotResumable;
        }

        if (!(await OpenAsync(job, workspace, cancellationToken)).TryGetValue(out var opened, out _))
        {
            return JobRejection.NotResumable;
        }

        if (!opened.Resumed || job.Recover(opened.Session, resumed: true).IsFailure)
        {
            _ = await agents.StopAsync(opened.Session, cancellationToken);

            return JobRejection.NotResumable;
        }

        await BeginAsync(job, opened.Session, RestartNote, cancellationToken);

        return new JobContinuation(job.Id, opened.Session, ContinuedIn.ResumedConversation);
    }

    public async Task<Result<JobContinuation, JobRejection>> ContinueAsync(Job job, Feedback guidance, CancellationToken cancellationToken) =>
        job.State == JobState.NeedsHelp
            ? await NextRoundAsync(job, guidance, Round.Hint, cancellationToken)
            : JobRejection.NotHeld;

    public async Task<Result<JobContinuation, JobRejection>> ContinueOnAsync(
        Job job,
        ConnectionName connection,
        Feedback guidance,
        CancellationToken cancellationToken)
    {
        if (job.State != JobState.NeedsHelp)
        {
            return JobRejection.NotHeld;
        }

        if (job.Connection == Option<ConnectionName>.Some(connection))
        {
            return JobRejection.SameConnection;
        }

        if (!(await planner.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return JobRejection.WorkspaceUnavailable;
        }

        if (!(await agents.OpenAsync(new AgentRequest(workspace.Path) { Connection = connection }, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            return Rejection(error);
        }

        var previous = job.Session;
        _ = job.ContinueOn(guidance, opened.Session, opened.Connection);
        await BeginAsync(job, opened.Session, $"{job.Instruction.Text}\n\n{guidance.Text}", cancellationToken);
        await previous.Match(
            async session => _ = await agents.StopAsync(session, cancellationToken),
            () => Task.CompletedTask);

        return new JobContinuation(job.Id, opened.Session, ContinuedIn.NewConversation);
    }

    public async Task RetryInSameSessionAsync(Job job, Feedback feedback, CancellationToken cancellationToken)
    {
        if (job.Retry(feedback).IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
            _ = await agents.TellAsync(job, feedback.Text, cancellationToken);
        }
    }

    public async Task<Result<JobContinuation, JobRejection>> HandOffAsync(Job job, JobHandoff handoff, CancellationToken cancellationToken) =>
        job.State == JobState.NeedsHelp
            ? await HandOffNowAsync(job, handoff, cancellationToken)
            : JobRejection.NotHeld;

    public async Task<Result<JobContinuation, JobRejection>> HandOffAfterTurnAsync(Job job, JobHandoff handoff, CancellationToken cancellationToken) =>
        job.State == JobState.Checking
            ? await HandOffNowAsync(job, handoff, cancellationToken)
            : JobRejection.NotRunning;

    private async Task<Result<JobContinuation, JobRejection>> HandOffNowAsync(Job job, JobHandoff handoff, CancellationToken cancellationToken)
    {
        if (job.Connection == Option<ConnectionName>.Some(handoff.Choice.Connection))
        {
            return JobRejection.SameConnection;
        }

        if (!Feedback.Create(handoff.Brief).TryGetValue(out var brief, out _))
        {
            return JobRejection.EmptyMessage;
        }

        if (!(await planner.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return JobRejection.WorkspaceUnavailable;
        }

        if (!(await agents.OpenAsync(new AgentRequest(workspace.Path) { Connection = handoff.Choice.Connection }, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            return Rejection(error);
        }

        var (previous, from) = (job.Session, job.Connection);

        if (job.HandOff(brief, opened.Session, opened.Connection).IsFailure)
        {
            _ = await agents.StopAsync(opened.Session, cancellationToken);

            return JobRejection.NotHeld;
        }

        await BeginAsync(job, opened.Session, brief.Text, cancellationToken);
        await previous.Match(
            async session => _ = await agents.StopAsync(session, cancellationToken),
            () => Task.CompletedTask);
        await from.Match(
            left => ledger.RecordHandoffAsync(job, left, opened.Session, handoff.Choice, cancellationToken),
            () => ledger.RecordChoiceAsync(job.Id, handoff.Choice, cancellationToken));

        return new JobContinuation(job.Id, opened.Session, ContinuedIn.NewConversation);
    }

    public async Task<Result<JobContinuation, JobRejection>> SendBackAsync(Job job, Feedback feedback, CancellationToken cancellationToken) =>
        job.State == JobState.AwaitingReview
            ? await NextRoundAsync(job, feedback, Round.SendBack, cancellationToken)
            : JobRejection.NotAwaitingReview;

    private async Task<Result<JobContinuation, JobRejection>> NextRoundAsync(Job job, Feedback guidance, Round round, CancellationToken cancellationToken)
    {
        var live = job.Session.Bind(session => agents.IsOpen(session) ? Option<SessionId>.Some(session) : Option<SessionId>.None);

        return await live.Match(
            session => ContinueInAsync(job, session, guidance, round, cancellationToken),
            () => ContinueInNewSessionAsync(job, guidance, round, cancellationToken));
    }

    private async Task<Result<JobContinuation, JobRejection>> ContinueInAsync(
        Job job,
        SessionId session,
        Feedback guidance,
        Round round,
        CancellationToken cancellationToken)
    {
        _ = round.InSameSession(job, guidance);
        await ledger.RecordAsync(job, cancellationToken);
        await TellAsync(job, guidance.Text, cancellationToken);

        return new JobContinuation(job.Id, session, ContinuedIn.SameSession);
    }

    private async Task<Result<JobContinuation, JobRejection>> ContinueInNewSessionAsync(
        Job job,
        Feedback guidance,
        Round round,
        CancellationToken cancellationToken)
    {
        if (!(await planner.FindAsync(job, cancellationToken)).TryGetValue(out var workspace, out _))
        {
            return JobRejection.WorkspaceUnavailable;
        }

        if (!(await OpenAsync(job, workspace, cancellationToken)).TryGetValue(out var opened, out var error))
        {
            return Rejection(error);
        }

        _ = round.InNewSession(job, guidance, opened.Session, opened.Resumed);
        await BeginAsync(
            job,
            opened.Session,
            opened.Resumed ? guidance.Text : $"{job.Instruction.Text}\n\n{guidance.Text}",
            cancellationToken);

        return new JobContinuation(job.Id, opened.Session, opened.Resumed ? ContinuedIn.ResumedConversation : ContinuedIn.NewConversation);
    }

    private async Task<Result<OpenedSession, AgentError>> OpenAsync(Job job, WorkspaceInfo workspace, CancellationToken cancellationToken) =>
        await agents.OpenAsync(new AgentRequest(workspace.Path) { Resume = job.Resume, Connection = job.Connection }, cancellationToken);

    private static JobRejection Rejection(AgentError error) => error switch
    {
        AgentError.UnknownConnection => JobRejection.UnknownConnection,
        AgentError.UnusableConnection => JobRejection.UnusableConnection,
        _ => JobRejection.AgentUnavailable,
    };

    private static FailureReason Failure(AgentError error) =>
        error is AgentError.UnknownConnection or AgentError.UnusableConnection ? FailureReason.ConnectionUnavailable : FailureReason.AgentUnavailable;

    private async Task BeginAsync(Job job, SessionId session, string message, CancellationToken cancellationToken)
    {
        await ledger.RecordSessionAsync(job, session, cancellationToken);
        await TellAsync(job, message, cancellationToken);
    }

    private async Task TellAsync(Job job, string message, CancellationToken cancellationToken)
    {
        if ((await messenger.TellAsync(job, message, cancellationToken)).IsFailure)
        {
            await FailAsync(job, FailureReason.AgentUnavailable, cancellationToken);
        }
    }

    private async Task FailAsync(Job job, FailureReason reason, CancellationToken cancellationToken)
    {
        if (job.Fail(reason, ledger.Now).IsSuccess)
        {
            await ledger.RecordAsync(job, cancellationToken);
        }
    }
}
