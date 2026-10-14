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
            Post(token => HeldAsync(integrationEvent.Hold, token));
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

    private async Task HeldAsync(JobHold hold, CancellationToken token)
    {
        var feedback = ImmutableInterlocked.TryRemove(ref pending, hold.Job, out var kept) ? Option<string>.Some(kept) : Option<string>.None;

        await (await situations.OfAsync(hold.Job, token)).Match(
            found =>
            {
                var situation = hold.Limit.Match(
                    limit => found with { Situation = found.Situation with { Own = found.Situation.Own.Reading(limit) } },
                    () => found);

                return ActAsync(situation, HandoffPolicy.WhenHeld(situation.Situation), feedback, situation.Situation.Now, token);
            },
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
                ? RungAsync(situation, HandoffPolicy.ByThreshold(situation.Situation), waiting, token)
                : EndAsync(job, token),
            () => EndAsync(job, token));
    }

    private async Task RungAsync(JobSituation situation, Decision decision, Waiting waiting, CancellationToken token)
    {
        if (decision.Verdict != Verdict.GoOn || situation.Situation.Rules.Resume == ResumeAtReset.Auto)
        {
            await ActAsync(situation, decision, waiting.Kept.Pending, waiting.Kept.Since, token);

            return;
        }

        await DisarmAsync(situation.Job);
        waits[situation.Job] = new Waiting(waiting.Kept, Option<ITimer>.None);
        var reset = actions.WaitOf(situation.Job).Match(
            kept => kept with { Connection = situation.Situation.Own.Connection },
            () => new ResetWait(situation.Job, situation.Situation.Own.Connection, string.Empty, Option<DateTimeOffset>.None, waiting.Kept.Since));
        await actions.WaitsAsync(reset with { AwaitsPerson = true, HasReset = true }, token);
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
            resume => WaitAsync(situation.Job, resume, feedback, since, situation.Situation.Rules.Resume == ResumeAtReset.Ask, token),
            () => EndAsync(situation.Job, token));
    }

    private async Task WaitAsync(JobId job, ResumePoint resume, Option<string> feedback, DateTimeOffset since, bool asks, CancellationToken token)
    {
        await DisarmAsync(job);
        var kept = new KeptWait(job, feedback, since);
        await store.KeepAsync(kept, token);
        waits[job] = new Waiting(kept, resume.At.Map(at => clock.CreateAlarm(() => Post(ringing => RingAsync(job, ringing)), at)));
        await actions.WaitsAsync(new ResetWait(job, resume.Connection, resume.Window, resume.At, since) { AwaitsPerson = asks }, token);
    }

    private async Task EndAsync(JobId job, CancellationToken token)
    {
        if (await DisarmAsync(job))
        {
            actions.Ended(job);
            await store.DropAsync(job, token);
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
