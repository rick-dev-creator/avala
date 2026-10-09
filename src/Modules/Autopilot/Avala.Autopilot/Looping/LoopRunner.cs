using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Autopilot.Loops;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Looping;

internal sealed class LoopRunner : IAsyncDisposable
{
    public const string ResetMessage = "The usage limit window has reset. Go on where you left off.";

    private readonly SerialExecutor executor = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly LoopSteps steps;
    private readonly LoopJournal journal;
    private readonly TimeProvider clock;
    private LoopRecord record;
    private Option<ITimer> wake;
    private bool ended;

    public LoopRunner(LoopRecord initial, LoopSteps steps, LoopJournal journal, TimeProvider clock)
    {
        record = initial;
        this.steps = steps;
        this.journal = journal;
        this.clock = clock;
        Id = initial.Id;
        Repository = initial.Repository;
    }

    public LoopId Id { get; }

    public string Repository { get; }

    public bool Ended => Volatile.Read(ref ended);

    public void Start() => Post(async token =>
    {
        await journal.PublishAsync(new LoopStarted(record.State), token);
        await AdvanceAsync(token);
    });

    public void Progressed(JobProgressed progressed) => Post(token => OnProgressedAsync(progressed, token));

    public void Held(JobHold hold) => Post(token => OnHeldAsync(hold, token));

    public void AskedAPerson(JobId job) => Post(async token =>
    {
        if (IsCurrent(job))
        {
            await SettleAsync(IterationOutcome.AwaitingAnswer, await steps.Approver.GatherAsync(job, token), Settlement.None, token);
        }
    });

    public Task<Result<LoopId, AutopilotError>> PauseAsync(CancellationToken cancellationToken) =>
        executor.RunAsync<Result<LoopId, AutopilotError>>(
            async token =>
            {
                if (record.Status is LoopStatus.Ended or LoopStatus.Paused)
                {
                    return record.Status == LoopStatus.Ended ? AutopilotError.LoopEnded : AutopilotError.NotRunning;
                }

                await DisarmAsync();
                var pause = new LoopPause(PauseReason.Command, clock.GetUtcNow(), Option<DateTimeOffset>.None);
                Change(record.Paused(pause));
                await journal.PublishAsync(new LoopPaused(Id, pause), token);

                return Id;
            },
            cancellationToken);

    public Task<Result<LoopId, AutopilotError>> ResumeAsync(CancellationToken cancellationToken) =>
        executor.RunAsync<Result<LoopId, AutopilotError>>(
            async token =>
            {
                if (record.Status != LoopStatus.Paused)
                {
                    return record.Status == LoopStatus.Ended ? AutopilotError.LoopEnded : AutopilotError.NotPaused;
                }

                await ResumeNowAsync(token);

                return Id;
            },
            cancellationToken);

    public Task<Result<LoopId, AutopilotError>> StopAsync(CancellationToken cancellationToken) =>
        executor.RunAsync<Result<LoopId, AutopilotError>>(
            async token =>
            {
                if (record.Status == LoopStatus.Ended)
                {
                    return AutopilotError.LoopEnded;
                }

                await EndAsync(record.Ended(LoopEnding.Stopped), token);

                return Id;
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await lifetime.CancelAsync();
        await executor.RunAsync(_ => DisarmAsync(), CancellationToken.None);
        await executor.DisposeAsync();
        lifetime.Dispose();
    }

    private async Task AdvanceAsync(CancellationToken token)
    {
        if (record.Status != LoopStatus.Running || record.Current.IsSome)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var limits = record.Request.Limits;
        var window = limits.SpendPerWindow.Count == 0 ? [] : await steps.Gauges.WithinAsync(now - limits.Window, now, token);
        var trip = record.TrippedBy(window, now);
        var exhausted = steps.Gauges.Exhausted(record.Connection, limits.PauseAtLimit, now);

        if (trip.IsSome || exhausted.IsSome)
        {
            await trip.Match(
                tripped => TripAsync(tripped, token),
                () => exhausted.Match(limit => PauseOrTripAsync(limit, now, token), () => Task.CompletedTask));
            return;
        }

        if (!(await steps.Sources.NextAsync(new SourceRequest(Id, Repository, now), token)).TryGetValue(out var answer, out var error))
        {
            await EndAsync(record.Failed(error), token);
            return;
        }

        await answer.Task.Match(
            task => TakeAsync(task, token),
            () => answer.NextDue.Match(due => WaitAsync(due, token), () => EndAsync(record.Ended(LoopEnding.Drained), token)));
    }

    private Task PauseOrTripAsync(UsageLimit limit, DateTimeOffset now, CancellationToken token) =>
        limit.ResetsAt.Match(
            until => PauseForLimitAsync(limit, until, continues: false, token),
            () => TripAsync(new BreakerTrip(Breaker.UsageLimit, limit.Window, (decimal)limit.UsedFraction, (decimal)record.Request.Limits.PauseAtLimit, now), token));

    private async Task TakeAsync(SourcedTask task, CancellationToken token)
    {
        var request = new JobRequest(Repository, task.Instruction, record.Request.AttemptsPerRound)
        {
            Autonomy = record.Request.Autonomy,
            Connection = record.Request.Connection,
        };
        var submitted = await steps.Jobs.SubmitAsync(request, token);
        var now = clock.GetUtcNow();
        var number = record.NextIteration;

        if (!submitted.TryGetValue(out var job, out var rejection))
        {
            var iteration = new IterationRecord(number, task, Option<JobId>.None, IterationOutcome.NotSubmitted, now)
            {
                Rejection = rejection,
                Failure = FailureSignatures.Rejected(rejection),
            };
            await steps.Sources.MarkAsync(task, new TaskMark(TaskState.Failed, Option<JobId>.None, now), token);
            Change(record.Settled(iteration, Option<ConnectionName>.None));
            await journal.PublishAsync(new LoopIterated(Id, iteration), token);
            Post(AdvanceAsync);
            return;
        }

        await steps.Sources.MarkAsync(task, new TaskMark(TaskState.Taken, job, now), token);
        Change(record.Took(task, job));
        await journal.PublishAsync(new LoopTaskTaken(Id, number, task, job), token);
    }

    private async Task OnProgressedAsync(JobProgressed progressed, CancellationToken token)
    {
        if (!IsCurrent(progressed.Job))
        {
            return;
        }

        switch (progressed.Status)
        {
            case JobStatus.AwaitingReview:
                await JudgeAsync(progressed.Job, token);
                break;
            case JobStatus.NeedsHelp:
                await HelpAsync(progressed.Job, token);
                break;
            case JobStatus.Failed or JobStatus.Approved or JobStatus.Discarded:
                await SettleAsync(Concluded(progressed.Status), await steps.Approver.GatherAsync(progressed.Job, token), Settlement.None, token);
                break;
        }
    }

    private async Task JudgeAsync(JobId job, CancellationToken token)
    {
        var evidence = await steps.Approver.GatherAsync(job, token);
        var decision = await steps.Approver.DecideAsync(Id, job, evidence, token);
        Change(record.Approved(decision));
        await SettleAsync(
            decision.Approved ? IterationOutcome.ApprovedAutomatically : IterationOutcome.AwaitingReview,
            evidence,
            Settlement.None with { Exceptions = decision.Exceptions, Rejection = decision.Refusal },
            token);
    }

    private async Task HelpAsync(JobId job, CancellationToken token)
    {
        var evidence = await steps.Approver.GatherAsync(job, token);

        if (evidence.Attempts.Count > 0 && evidence.Attempts[^1].Outcome == AttemptOutcome.Interrupted)
        {
            return;
        }

        await SettleAsync(IterationOutcome.NeedsHelp, evidence, Settlement.None, token);
    }

    private async Task OnHeldAsync(JobHold hold, CancellationToken token)
    {
        if (!IsCurrent(hold.Job))
        {
            return;
        }

        var evidence = await steps.Approver.GatherAsync(hold.Job, token);
        var reset = hold.Reason == HoldReason.LimitNearlyReached
            ? steps.Gauges.NextReset(evidence.Connection.IsSome ? evidence.Connection : record.Connection, clock.GetUtcNow())
            : Option<UsageLimit>.None;

        await reset.Match(
            limit => PauseForLimitAsync(limit, limit.ResetsAt.Match(at => at, clock.GetUtcNow), continues: true, token),
            () => SettleAsync(IterationOutcome.NeedsHelp, evidence, Settlement.None with { Hold = hold.Reason }, token));
    }

    private Task SettleAsync(IterationOutcome outcome, JobEvidence evidence, Settlement settlement, CancellationToken token) =>
        record.Current.Match(underway => SettleAsync(underway, outcome, evidence, settlement, token), () => Task.CompletedTask);

    private async Task SettleAsync(Underway underway, IterationOutcome outcome, JobEvidence evidence, Settlement settlement, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        var iteration = new IterationRecord(underway.Iteration, underway.Task, underway.Job, outcome, now)
        {
            Exceptions = settlement.Exceptions,
            Hold = settlement.Hold,
            Rejection = settlement.Rejection,
            Failure = outcome.IsFailure ? FailureSignatures.Of(outcome, settlement.Hold, evidence.Latest) : Option<FailureSignature>.None,
            ChangedNothing = evidence.ChangedNothing,
            Cost = steps.Gauges.SpendOf(underway.Job),
        };
        await steps.Sources.MarkAsync(underway.Task, new TaskMark(StateOf(outcome), underway.Job, now), token);
        Change(record.Settled(iteration, evidence.Connection));
        await journal.PublishAsync(new LoopIterated(Id, iteration), token);
        await AdvanceAsync(token);
    }

    private async Task PauseForLimitAsync(UsageLimit limit, DateTimeOffset until, bool continues, CancellationToken token)
    {
        var pause = new LoopPause(PauseReason.UsageLimit, clock.GetUtcNow(), until) { Window = limit.Window };
        var paused = record.Paused(pause);
        Change(continues ? paused.Awaiting(underway => underway with { ContinuesAfterReset = true }) : paused);
        await journal.PublishAsync(new LoopPaused(Id, pause), token);
        await ArmAsync(until, ResumeAfterResetAsync);
    }

    private async Task ResumeAfterResetAsync(CancellationToken token)
    {
        if (record.Status == LoopStatus.Paused && record.Pause == Option<PauseReason>.Some(PauseReason.UsageLimit))
        {
            await ResumeNowAsync(token);
        }
    }

    private async Task ResumeNowAsync(CancellationToken token)
    {
        await DisarmAsync();
        Change(record.Resumed());
        var continuing = record.Current.Bind(underway => underway.ContinuesAfterReset ? Option<Underway>.Some(underway) : Option<Underway>.None);
        await continuing.Match(underway => ContinueAsync(underway, token), async () =>
        {
            await journal.PublishAsync(new LoopResumed(Id, clock.GetUtcNow()), token);
            await AdvanceAsync(token);
        });
    }

    private async Task ContinueAsync(Underway underway, CancellationToken token)
    {
        var continued = await steps.Jobs.ContinueAsync(underway.Job, ResetMessage, token);
        await journal.PublishAsync(new LoopResumed(Id, clock.GetUtcNow()), token);

        if (continued.TryGetValue(out _, out var rejection))
        {
            Change(record.Awaiting(current => current with { ContinuesAfterReset = false }));
            return;
        }

        await SettleAsync(
            IterationOutcome.NeedsHelp,
            await steps.Approver.GatherAsync(underway.Job, token),
            Settlement.None with { Hold = HoldReason.LimitNearlyReached, Rejection = rejection },
            token);
    }

    private async Task WaitAsync(DateTimeOffset due, CancellationToken token)
    {
        Change(record.Waiting(due));
        await journal.PublishAsync(new LoopWaiting(Id, due), token);
        await ArmAsync(due, WakeAsync);
    }

    private async Task WakeAsync(CancellationToken token)
    {
        if (record.Status == LoopStatus.Waiting)
        {
            Change(record.Resumed());
            await AdvanceAsync(token);
        }
    }

    private async Task TripAsync(BreakerTrip trip, CancellationToken token)
    {
        await journal.PublishAsync(new BreakerTripped(Id, trip), token);
        await EndAsync(record.Tripped(trip), token);
    }

    private async Task EndAsync(LoopRecord next, CancellationToken token)
    {
        await DisarmAsync();
        Change(next);
        await journal.PublishAsync(new LoopEnded(next.State), token);
    }

    private void Change(LoopRecord next)
    {
        record = next;
        journal.Keep(next);
        Volatile.Write(ref ended, next.Status == LoopStatus.Ended);
    }

    private bool IsCurrent(JobId job) => record.Current.Match(underway => underway.Job == job, () => false);

    private async Task ArmAsync(DateTimeOffset due, Func<CancellationToken, Task> work)
    {
        await DisarmAsync();
        var wait = due - clock.GetUtcNow();
        wake = Option<ITimer>.Some(clock.CreateTimer(_ => Post(work), null, wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Timeout.InfiniteTimeSpan));
    }

    private async Task DisarmAsync()
    {
        var armed = wake;
        wake = Option<ITimer>.None;
        await armed.Match(timer => timer.DisposeAsync().AsTask(), () => Task.CompletedTask);
    }

    private void Post(Func<CancellationToken, Task> work) =>
        _ = executor.RunAsync(token => GuardedAsync(work, token), lifetime.Token);

    private async Task GuardedAsync(Func<CancellationToken, Task> work, CancellationToken token)
    {
        try
        {
            await work(token);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
        {
            journal.Failed(Id, exception);
        }
    }

    private static IterationOutcome Concluded(JobStatus status) => status switch
    {
        JobStatus.Failed => IterationOutcome.Failed,
        JobStatus.Approved => IterationOutcome.ApprovedByPerson,
        _ => IterationOutcome.Discarded,
    };

    private static TaskState StateOf(IterationOutcome outcome) => outcome switch
    {
        IterationOutcome.ApprovedAutomatically or IterationOutcome.ApprovedByPerson => TaskState.Approved,
        IterationOutcome.AwaitingReview or IterationOutcome.AwaitingAnswer or IterationOutcome.NeedsHelp => TaskState.WaitingForPerson,
        _ => TaskState.Failed,
    };

    private sealed record Settlement(IReadOnlyList<ExceptionReason> Exceptions, Option<HoldReason> Hold, Option<JobRejection> Rejection)
    {
        public static Settlement None { get; } = new([], Option<HoldReason>.None, Option<JobRejection>.None);
    }
}
