using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Evidence;
using Avala.Autopilot.Loops;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Looping;

internal sealed class LoopRunner : IAsyncDisposable
{
    public const string ResetMessage = "The usage limit window has reset. Go on where you left off.";

    public const string NewDayMessage = "A new day has begun, so the daily spend ceiling allows work again. Go on where you left off.";

    private readonly LoopLine line;
    private readonly LoopSteps steps;
    private readonly LoopJournal journal;
    private readonly TimeProvider clock;
    private LoopRecord record;
    private bool ended;

    public LoopRunner(LoopRecord initial, LoopSteps steps, LoopJournal journal, TimeProvider clock)
    {
        record = initial;
        this.steps = steps;
        this.journal = journal;
        this.clock = clock;
        Id = initial.Id;
        Repository = initial.Repository;
        line = new LoopLine(clock, FaultAsync);
    }

    public LoopId Id { get; }

    public string Repository { get; }

    public bool Ended => Volatile.Read(ref ended);

    public void Start() => line.Post(async token =>
    {
        await journal.PublishAsync(new LoopStarted(record.State), token);
        await AdvanceAsync(token);
    });

    public void Resume() => line.Post(RecoverAsync);

    public void Progressed(JobProgressed progressed) => line.Post(token => OnStatusAsync(progressed.Job, progressed.Status, token));

    public void Held(JobHold hold) => line.Post(token => OnHeldAsync(hold, token));

    public void Intervened(BudgetIntervention intervention) => line.Post(token => OnInterventionAsync(intervention, token));

    public void AskedAPerson(JobId job) => line.Post(async token =>
    {
        if (IsCurrent(job))
        {
            await SettleAsync(IterationOutcome.AwaitingAnswer, await steps.Approver.GatherAsync(job, token), Settlement.None, token);
        }
    });

    public Task<Result<LoopId, AutopilotError>> PauseAsync(CancellationToken cancellationToken) =>
        line.CommandAsync(
            async token =>
            {
                if (record.Status is LoopStatus.Ended or LoopStatus.Paused)
                {
                    return record.Status == LoopStatus.Ended ? AutopilotError.LoopEnded : AutopilotError.NotRunning;
                }

                await line.DisarmAsync();
                var pause = new LoopPause(PauseReason.Command, clock.GetUtcNow(), Option<DateTimeOffset>.None);
                await ChangeAsync(record.Paused(pause), token);
                await journal.PublishAsync(new LoopPaused(Id, pause), token);

                return Id;
            },
            cancellationToken);

    public Task<Result<LoopId, AutopilotError>> ResumeAsync(CancellationToken cancellationToken) =>
        line.CommandAsync(
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
        line.CommandAsync(
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

    public ValueTask DisposeAsync() => line.DisposeAsync();

    private async Task AdvanceAsync(CancellationToken token)
    {
        if (record.Status != LoopStatus.Running || record.Current.IsSome)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var limits = record.Limits;
        var window = limits.SpendPerWindow.Count == 0 ? [] : await steps.Gauges.WithinAsync(now - limits.Window, now, token);
        var trip = record.TrippedBy(window, now);
        var exhausted = await steps.Gauges.ExhaustedAsync(record.Request.Connection, limits.PauseAtLimit, now, token);

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
            until => PauseUntilAsync(LimitPause(limit, until), continues: false, token),
            () => TripAsync(new BreakerTrip(Breaker.UsageLimit, limit.Window, (decimal)limit.UsedFraction, (decimal)record.Limits.PauseAtLimit, now), token));

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
            await ChangeAsync(record.Settled(iteration, Option<ConnectionName>.None), token);
            await journal.PublishAsync(new LoopIterated(Id, iteration), token);
            line.Post(AdvanceAsync);
            return;
        }

        await steps.Sources.MarkAsync(task, new TaskMark(TaskState.Taken, job, now), token);
        await ChangeAsync(record.Took(task, job), token);
        await journal.PublishAsync(new LoopTaskTaken(Id, number, task, job), token);
    }

    private Task RecoverAsync(CancellationToken token) => record switch
    {
        { Status: LoopStatus.Waiting } => line.ArmAsync(record.Until.Match(until => until, clock.GetUtcNow), WakeAsync),
        _ when AwaitsReset => RecoverHeldAsync(CurrentJob, token),
        _ when PausedUntilATime => line.ArmAsync(record.Until.Match(until => until, clock.GetUtcNow), ResumeAfterResetAsync),
        { Current.IsSome: true } => ReconcileAsync(CurrentJob, token),
        { Status: LoopStatus.Running } => AdvanceAsync(token),
        _ => Task.CompletedTask,
    };

    private async Task RecoverHeldAsync(JobId job, CancellationToken token)
    {
        var status = await steps.Approver.StatusAsync(job, token);

        if (status == Option<JobStatus>.Some(JobStatus.NeedsHelp))
        {
            await line.ArmAsync(record.Until.Match(until => until, clock.GetUtcNow), ResumeAfterResetAsync);
            return;
        }

        await RunsAgainAsync(token);
        await ReconcileAsync(job, status, token);
    }

    private async Task ReconcileAsync(JobId job, CancellationToken token) => await ReconcileAsync(job, await steps.Approver.StatusAsync(job, token), token);

    private async Task ReconcileAsync(JobId job, Option<JobStatus> status, CancellationToken token)
    {
        if (status == Option<JobStatus>.Some(JobStatus.NeedsHelp))
        {
            await SettleAsync(IterationOutcome.NeedsHelp, await steps.Approver.GatherAsync(job, token), Settlement.None, token);
        }
        else if (status.IsNone)
        {
            await SettleAsync(IterationOutcome.Failed, JobEvidence.None, Settlement.None, token);
        }
        else
        {
            await OnStatusAsync(job, status.Match(known => known, () => JobStatus.Failed), token);
        }
    }

    private async Task OnStatusAsync(JobId job, JobStatus status, CancellationToken token)
    {
        if (!IsCurrent(job))
        {
            await FollowAsync(job, status, token);
            return;
        }

        switch (status)
        {
            case JobStatus.Running when AwaitsReset:
                await RunsAgainAsync(token);
                break;
            case JobStatus.AwaitingReview:
                await JudgeAsync(job, token);
                break;
            case JobStatus.NeedsHelp:
                await HelpAsync(job, token);
                break;
            case JobStatus.Failed or JobStatus.Approved or JobStatus.Discarded:
                await SettleAsync(Concluded(status), await steps.Approver.GatherAsync(job, token), Settlement.None, token);
                break;
        }
    }

    private async Task FollowAsync(JobId job, JobStatus status, CancellationToken token)
    {
        foreach (var iteration in record.Following(job, status, steps.Gauges.SpendOf(job)).Match<IterationRecord[]>(moved => [moved], () => []))
        {
            await ChangeAsync(record.Followed(iteration), token);
            await journal.PublishAsync(new LoopIterationFollowed(Id, iteration), token);
        }
    }

    private async Task JudgeAsync(JobId job, CancellationToken token)
    {
        var evidence = await steps.Approver.GatherAsync(job, token);
        var decision = await steps.Approver.DecideAsync(Id, job, evidence, token);
        await ChangeAsync(record.Approved(decision), token);
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

        if (hold.Reason == HoldReason.BudgetExceeded)
        {
            await steps.Gauges.InterventionOf(hold).Match(found => OnInterventionAsync(found, token), () => Task.CompletedTask);
            return;
        }

        var evidence = await steps.Approver.GatherAsync(hold.Job, token);
        var reset = hold.Reason == HoldReason.LimitNearlyReached ? WindowOf(hold, evidence) : Option<UsageLimit>.None;

        await reset.Match(
            limit => PauseUntilAsync(LimitPause(limit, limit.ResetsAt.Match(at => at, clock.GetUtcNow)), continues: true, token),
            () => SettleAsync(IterationOutcome.NeedsHelp, evidence, Settlement.None with { Hold = hold.Reason }, token));
    }

    private Option<UsageLimit> WindowOf(JobHold hold, JobEvidence evidence) =>
        hold.Limit.Bind(limit => limit.ResetsAt.IsSome ? Option<UsageLimit>.Some(limit) : Option<UsageLimit>.None) is { IsSome: true } held
            ? held
            : steps.Gauges.NextReset(evidence.Connection.IsSome ? evidence.Connection : record.Connection, clock.GetUtcNow());

    private async Task OnInterventionAsync(BudgetIntervention intervention, CancellationToken token)
    {
        var hold = intervention.Hold;

        if (hold.Reason != HoldReason.BudgetExceeded || !IsCurrent(hold.Job) || AwaitsReset)
        {
            return;
        }

        if (intervention.Breach.Measure == BudgetMeasure.DailySpend)
        {
            var now = clock.GetUtcNow();
            await PauseUntilAsync(new LoopPause(PauseReason.DailySpend, now, NextDay(now, clock.LocalTimeZone)), continues: true, token);
            return;
        }

        await SettleAsync(IterationOutcome.NeedsHelp, await steps.Approver.GatherAsync(hold.Job, token), Settlement.None with { Hold = hold.Reason }, token);
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
        await ChangeAsync(record.Settled(iteration, evidence.Connection), token);
        await journal.PublishAsync(new LoopIterated(Id, iteration), token);
        await AdvanceAsync(token);
    }

    private LoopPause LimitPause(UsageLimit limit, DateTimeOffset until) => new(PauseReason.UsageLimit, clock.GetUtcNow(), until) { Window = limit.Window };

    private async Task PauseUntilAsync(LoopPause pause, bool continues, CancellationToken token)
    {
        var paused = record.Paused(pause);
        await ChangeAsync(continues ? paused.Awaiting(underway => underway with { ContinuesAfterReset = true }) : paused, token);
        await line.ArmAsync(pause.Until.Match(until => until, clock.GetUtcNow), ResumeAfterResetAsync);
        await journal.PublishAsync(new LoopPaused(Id, pause), token);
    }

    private async Task ResumeAfterResetAsync(CancellationToken token)
    {
        if (PausedUntilATime)
        {
            await ResumeNowAsync(token);
        }
    }

    private async Task ResumeNowAsync(CancellationToken token)
    {
        var reason = record.Pause;
        await line.DisarmAsync();
        await ChangeAsync(record.Resumed(), token);
        var continuing = record.Current.Bind(underway => underway.ContinuesAfterReset ? Option<Underway>.Some(underway) : Option<Underway>.None);
        await continuing.Match(underway => ContinueAsync(underway, reason == Option<PauseReason>.Some(PauseReason.DailySpend), token), async () =>
        {
            await journal.PublishAsync(new LoopResumed(Id, clock.GetUtcNow()), token);
            await AdvanceAsync(token);
        });
    }

    private bool PausedUntilATime =>
        record.Status == LoopStatus.Paused && record.Pause.Match(reason => reason is PauseReason.UsageLimit or PauseReason.DailySpend, () => false);

    private bool AwaitsReset => PausedUntilATime && record.Current.Match(underway => underway.ContinuesAfterReset, () => false);

    private JobId CurrentJob => record.Current.Match(underway => underway.Job, () => default);

    private async Task RunsAgainAsync(CancellationToken token)
    {
        await line.DisarmAsync();
        await ChangeAsync(record.Resumed().Awaiting(current => current with { ContinuesAfterReset = false }), token);
        await journal.PublishAsync(new LoopResumed(Id, clock.GetUtcNow()), token);
    }

    private async Task ContinueAsync(Underway underway, bool newDay, CancellationToken token)
    {
        var continued = await steps.Jobs.ContinueAsync(underway.Job, new JobMessage(newDay ? NewDayMessage : ResetMessage) { Author = MessageAuthor.Avala, CausedBy = MessageAuthor.Avala }, token);
        await journal.PublishAsync(new LoopResumed(Id, clock.GetUtcNow()), token);

        if (continued.TryGetValue(out _, out var rejection) || rejection == JobRejection.NotHeld)
        {
            await ChangeAsync(record.Awaiting(current => current with { ContinuesAfterReset = false }), token);
            return;
        }

        await SettleAsync(
            IterationOutcome.NeedsHelp,
            await steps.Approver.GatherAsync(underway.Job, token),
            Settlement.None with { Hold = newDay ? HoldReason.BudgetExceeded : HoldReason.LimitNearlyReached, Rejection = rejection },
            token);
    }

    private async Task WaitAsync(DateTimeOffset due, CancellationToken token)
    {
        await ChangeAsync(record.Waiting(due), token);
        await line.ArmAsync(due, WakeAsync);
        await journal.PublishAsync(new LoopWaiting(Id, due), token);
    }

    private async Task WakeAsync(CancellationToken token)
    {
        if (record.Status == LoopStatus.Waiting)
        {
            await ChangeAsync(record.Resumed(), token);
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
        await line.DisarmAsync();
        await ChangeAsync(next, token);
        await journal.PublishAsync(new LoopEnded(next.State), token);
    }

    private async Task ChangeAsync(LoopRecord next, CancellationToken token)
    {
        record = next;
        Volatile.Write(ref ended, next.Status == LoopStatus.Ended);
        await journal.KeepAsync(next, token);
    }

    private bool IsCurrent(JobId job) => record.Current.Match(underway => underway.Job == job, () => false);

    private async Task FaultAsync(Exception exception, CancellationToken token)
    {
        journal.Failed(Id, exception);

        if (record.Status == LoopStatus.Ended)
        {
            return;
        }

        try
        {
            await EndAsync(record.Faulted($"{exception.GetType().Name}: {exception.Message}"), token);
        }
        catch (Exception ending) when (ending is not OperationCanceledException || !token.IsCancellationRequested)
        {
            journal.Failed(Id, ending);
        }
    }

    private static DateTimeOffset NextDay(DateTimeOffset now, TimeZoneInfo zone)
    {
        var midnight = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(1);

        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
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
