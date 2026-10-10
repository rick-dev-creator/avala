using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Forges.Watching;

internal sealed class PullRequestWatcher(WatchBook book, PullRequestReader reader, Waker waker, TimeProvider clock)
    : IHandle<JobProgressed>, IHandle<PullRequestOpened>, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor executor = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<JobId, ITimer> timers = [];
    private int disposed;

    public StartupStage Stage => StartupStage.Recovery;

    public Task RunAsync(CancellationToken cancellationToken) =>
        executor.RunAsync(
            _ =>
            {
                foreach (var kept in book.Live)
                {
                    Post(token => PollAsync(kept.State.Job, token));
                }

                return Task.CompletedTask;
            },
            cancellationToken);

    public ValueTask HandleAsync(PullRequestOpened integrationEvent, CancellationToken cancellationToken)
    {
        Post(_ => ArmAsync(integrationEvent.Job));

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (book.Find(integrationEvent.Job).Match(kept => kept.IsLive, () => false))
        {
            Post(token => ProgressedAsync(integrationEvent.Job, integrationEvent.Status, token));
        }

        return ValueTask.CompletedTask;
    }

    public async Task<Result<PullRequestWatchState, ForgeError>> RefreshAsync(JobId job, CancellationToken cancellationToken) =>
        await executor.RunAsync(
            async token =>
            {
                if (LiveWatch(job).IsSome)
                {
                    await PollAsync(job, token);
                }

                return book.Find(job).Map(kept => kept.State).ToResult(ForgeError.NotFound);
            },
            cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await lifetime.CancelAsync();
        await executor.RunAsync(
            _ =>
            {
                foreach (var timer in timers.Values)
                {
                    timer.Dispose();
                }

                timers.Clear();

                return Task.CompletedTask;
            },
            CancellationToken.None);
        await executor.DisposeAsync();
        lifetime.Dispose();
    }

    private Task ArmAsync(JobId job)
    {
        LiveWatch(job).Match(
            kept =>
            {
                var due = kept.State.NextPoll.Match(next => next - clock.GetUtcNow(), () => TimeSpan.Zero);
                Arm(job, due > TimeSpan.Zero ? due : TimeSpan.Zero);

                return true;
            },
            () => false);

        return Task.CompletedTask;
    }

    private Task ProgressedAsync(JobId job, JobStatus status, CancellationToken token) =>
        LiveWatch(job).Match(kept => ProgressedAsync(kept, status, token), () => Task.CompletedTask);

    private async Task ProgressedAsync(KeptWatch kept, JobStatus status, CancellationToken token)
    {
        var job = kept.State.Job;

        switch (status)
        {
            case JobStatus.AwaitingReview when kept.State.WakeUps > 0:
                _ = await waker.RedeliverAsync(job, token);
                break;
            case JobStatus.Discarded or JobStatus.Failed:
                Disarm(job);
                await book.SaveAsync(kept with { State = kept.State with { Status = WatchStatus.Ended, Ended = WatchEnd.JobEnded, NextPoll = Option<DateTimeOffset>.None } }, token);
                break;
            case JobStatus.Running or JobStatus.Checking or JobStatus.NeedsHelp or JobStatus.AwaitingReview when kept.State.Status != WatchStatus.WaitingForJob:
                await book.SaveAsync(kept with { State = kept.State with { Status = WatchStatus.WaitingForJob } }, token);
                break;
        }
    }

    private Option<KeptWatch> LiveWatch(JobId job) =>
        book.Find(job).Bind(kept => kept.IsLive ? Option<KeptWatch>.Some(kept) : Option<KeptWatch>.None);

    private Task PollAsync(JobId job, CancellationToken token)
    {
        Disarm(job);

        return LiveWatch(job).Match(kept => PollAsync(kept, token), () => Task.CompletedTask);
    }

    private async Task PollAsync(KeptWatch kept, CancellationToken token)
    {
        var job = kept.State.Job;
        var observed = await reader.ReadAsync(kept, token);
        var poll = await reader.PollAsync(token);
        var now = clock.GetUtcNow();

        if (!observed.TryGetValue(out var pullRequest, out var error))
        {
            var failures = kept.State.Failures + 1;
            var wait = Backoff.After(poll, failures);
            Arm(job, wait);
            await book.SaveAsync(
                kept with { State = kept.State with { Status = WatchStatus.BackingOff, Failure = error, Failures = failures, LastPolled = now, NextPoll = now + wait } },
                token);

            return;
        }

        var status = await waker.StatusAsync(job, token);
        var decision = WatchPolicy.Decide(kept.State, kept.Handled.ToHashSet(StringComparer.Ordinal), pullRequest, status.Match(found => found, () => JobStatus.Failed));
        var observedState = kept.State with
        {
            Last = pullRequest,
            LastPolled = now,
            Failure = Option<ForgeError>.None,
            Failures = 0,
            Pending = decision.Trigger.Map(trigger => trigger.Reason),
        };

        await ActAsync(kept with { State = observedState }, decision, pullRequest, poll, token);
    }

    private async Task ActAsync(KeptWatch kept, WatchDecision decision, PullRequestState observed, TimeSpan poll, CancellationToken token)
    {
        var job = kept.State.Job;
        var next = clock.GetUtcNow() + poll;

        switch (decision.Verdict)
        {
            case Verdict.End:
                await book.SaveAsync(kept with { State = kept.State with { Status = WatchStatus.Ended, Ended = decision.End, NextPoll = Option<DateTimeOffset>.None } }, token);
                break;
            case Verdict.Wake:
                await decision.Trigger.Match(trigger => WakeAsync(kept, trigger, observed, poll, token), () => Task.CompletedTask);
                break;
            case Verdict.HoldForPerson:
                await decision.Trigger.Match(trigger => HoldAsync(kept, trigger, poll, token), () => Task.CompletedTask);
                break;
            default:
                var waiting = decision.Verdict == Verdict.WaitForJob ? WatchStatus.WaitingForJob
                    : kept.State.Status == WatchStatus.NeedsPerson ? WatchStatus.NeedsPerson
                    : WatchStatus.Watching;
                Arm(job, poll);
                await book.SaveAsync(kept with { State = kept.State with { Status = waiting, NextPoll = next } }, token);
                break;
        }
    }

    private async Task WakeAsync(KeptWatch kept, Trigger trigger, PullRequestState observed, TimeSpan poll, CancellationToken token)
    {
        var feedback = WakeFeedback.For(trigger, observed, kept.Remote);
        var woken = await waker.WakeAsync(kept, trigger, feedback, token);
        var now = clock.GetUtcNow();
        var record = new WakeUpRecord(kept.State.Job, kept.State.PullRequest.Number, trigger.Reason, trigger.Key, feedback, now, woken.IsSuccess ? WakeOutcome.Woken : WakeOutcome.Refused)
        {
            Conversation = woken.Match(continued => Option<ContinuedIn>.Some(continued.Conversation), _ => Option<ContinuedIn>.None),
            Refusal = woken.Match(_ => Option<JobRejection>.None, Option<JobRejection>.Some),
        };

        Arm(kept.State.Job, poll);
        await book.AddAsync(record, token);
        await book.SaveAsync(
            kept with
            {
                State = kept.State with
                {
                    WakeUps = kept.State.WakeUps + 1,
                    Status = woken.IsSuccess ? WatchStatus.WaitingForJob : WatchStatus.Watching,
                    NextPoll = now + poll,
                },
                Handled = [.. kept.Handled, trigger.Key],
            },
            token);
    }

    private async Task HoldAsync(KeptWatch kept, Trigger trigger, TimeSpan poll, CancellationToken token)
    {
        var comment = WakeFeedback.HeldComment(kept.State.WakeUps, trigger.Reason);
        _ = await reader.CommentAsync(kept, comment, token);
        var now = clock.GetUtcNow();

        Arm(kept.State.Job, poll);
        await book.AddAsync(new WakeUpRecord(kept.State.Job, kept.State.PullRequest.Number, trigger.Reason, trigger.Key, comment, now, WakeOutcome.HeldForPerson), token);
        await book.SaveAsync(kept with { State = kept.State with { Status = WatchStatus.NeedsPerson, NextPoll = now + poll } }, token);
    }

    private void Arm(JobId job, TimeSpan due)
    {
        Disarm(job);
        timers[job] = clock.CreateTimer(_ => Post(token => PollAsync(job, token)), null, due, Timeout.InfiniteTimeSpan);
    }

    private void Disarm(JobId job)
    {
        if (timers.Remove(job, out var timer))
        {
            timer.Dispose();
        }
    }

    private void Post(Func<CancellationToken, Task> work)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            _ = executor.RunAsync(work, lifetime.Token);
        }
    }
}
