using System.Collections.Immutable;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Handoffs.Watching;

internal sealed class LimitWatch(Situations situations, LimitActions actions, IHandoffStore store, TimeProvider clock)
    : IHandle<JobHeld>, IHandle<JobProgressed>, IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor executor = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<JobId, Waiting> waits = [];
    private ImmutableDictionary<JobId, string> pending = ImmutableDictionary<JobId, string>.Empty;
    private int disposed;

    public StartupStage Stage => StartupStage.Recovery;

    public void Pending(JobId job, string feedback) => ImmutableInterlocked.Update(ref pending, kept => kept.SetItem(job, feedback));

    public Task RunAsync(CancellationToken cancellationToken) =>
        executor.RunAsync(
            async token =>
            {
                foreach (var kept in await store.WaitsAsync(token))
                {
                    await RestoreAsync(kept, token);
                }
            },
            cancellationToken);

    public ValueTask HandleAsync(JobHeld integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Hold.Reason == HoldReason.LimitNearlyReached)
        {
            Post(token => HeldAsync(integrationEvent.Hold.Job, token));
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask HandleAsync(JobProgressed integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent.Status != JobStatus.NeedsHelp)
        {
            ImmutableInterlocked.Update(ref pending, kept => kept.Remove(integrationEvent.Job));
            Post(token => EndAsync(integrationEvent.Job, token));
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        await lifetime.CancelAsync();
        await executor.RunAsync(_ => DisarmAllAsync(), CancellationToken.None);
        await executor.DisposeAsync();
        lifetime.Dispose();
    }

    private async Task HeldAsync(JobId job, CancellationToken token)
    {
        var feedback = ImmutableInterlocked.TryRemove(ref pending, job, out var kept) ? Option<string>.Some(kept) : Option<string>.None;

        await (await situations.OfAsync(job, token)).Match(
            situation => ActAsync(situation, HandoffPolicy.WhenHeld(situation.Situation), feedback, situation.Situation.Now, token),
            () => Task.CompletedTask);
    }

    private async Task RestoreAsync(KeptWait kept, CancellationToken token)
    {
        waits[kept.Job] = new Waiting(kept, Option<ITimer>.None);
        await RingAsync(kept.Job, token);
    }

    private async Task RingAsync(JobId job, CancellationToken token)
    {
        if (!waits.TryGetValue(job, out var waiting))
        {
            return;
        }

        await (await situations.OfAsync(job, token)).Match(
            situation => situation.History.Summary.Status == JobStatus.NeedsHelp
                ? ActAsync(situation, HandoffPolicy.ByThreshold(situation.Situation), waiting.Kept.Pending, waiting.Kept.Since, token)
                : EndAsync(job, token),
            () => EndAsync(job, token));
    }

    private async Task ActAsync(JobSituation situation, Decision decision, Option<string> feedback, DateTimeOffset since, CancellationToken token)
    {
        var moved = decision.Verdict switch
        {
            Verdict.GoOn => await actions.ContinueAsync(situation, feedback, token),
            Verdict.HandOff => await actions.HandOffAsync(situation, decision, feedback, token),
            _ => false,
        };

        if (moved)
        {
            await EndAsync(situation.Job, token);
            return;
        }

        await decision.Resume.Match(
            resume => WaitAsync(situation.Job, resume, feedback, since, token),
            () => EndAsync(situation.Job, token));
    }

    private async Task WaitAsync(JobId job, ResumePoint resume, Option<string> feedback, DateTimeOffset since, CancellationToken token)
    {
        await DisarmAsync(job);
        var kept = new KeptWait(job, feedback, since);
        await store.KeepAsync(kept, token);
        var due = resume.At.Map(at => at - clock.GetUtcNow());
        waits[job] = new Waiting(kept, due.Map(wait => clock.CreateTimer(_ => Post(ringing => RingAsync(job, ringing)), null, wait > TimeSpan.Zero ? wait : TimeSpan.Zero, Timeout.InfiniteTimeSpan)));
        await actions.WaitsAsync(new ResetWait(job, resume.Connection, resume.Window, resume.At, since), token);
    }

    private async Task EndAsync(JobId job, CancellationToken token)
    {
        if (await DisarmAsync(job))
        {
            await store.DropAsync(job, token);
            actions.Ended(job);
        }
    }

    private async Task<bool> DisarmAsync(JobId job)
    {
        if (!waits.Remove(job, out var waiting))
        {
            return false;
        }

        await waiting.Timer.Match(timer => timer.DisposeAsync().AsTask(), () => Task.CompletedTask);

        return true;
    }

    private async Task DisarmAllAsync()
    {
        foreach (var job in waits.Keys.ToList())
        {
            _ = await DisarmAsync(job);
        }
    }

    private void Post(Func<CancellationToken, Task> work)
    {
        if (Volatile.Read(ref disposed) == 0)
        {
            _ = executor.RunAsync(work, lifetime.Token);
        }
    }

    private sealed record Waiting(KeptWait Kept, Option<ITimer> Timer);
}
