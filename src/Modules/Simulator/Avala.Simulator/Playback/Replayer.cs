using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class Replayer(SessionOptions options, IFileWriter files, Gates gates, Pacing pacing)
{
    private Pending<PermissionDecision>? permission;
    private Pending<FormAnswer>? form;
    private bool started;

    public bool Closing { get; private set; }

    public bool AwaitsInterrupt { get; private set; }

    public async IAsyncEnumerable<IAgentEvent> PlayAsync(
        Cues cues,
        Conversation conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var play = new Play(cues, conversation.Scenario.AsRecorded, conversation.Advanced.Token);
        permission = null;
        form = null;
        started = false;
        AwaitsInterrupt = false;

        try
        {
            var script = conversation.NextScript;

            for (var next = 0; next < script.Count; next++)
            {
                var played = await StepAsync(script[next], script.Skip(next), play, cancellationToken);

                foreach (var cue in played.Cues)
                {
                    yield return cue;
                }

                if (played.Ends)
                {
                    Closing = true;

                    yield break;
                }
            }
        }
        finally
        {
            await WithdrawAsync();
        }
    }

    private Task<Played> StepAsync(IStep step, IEnumerable<IStep> ahead, Play play, CancellationToken cancellationToken) =>
        Unanswered(ahead) is { } unanswered
            ? Task.FromResult(Diverged(play, unanswered))
            : ActAsync(step, play, cancellationToken);

    private Task<Played> ActAsync(IStep step, Play play, CancellationToken cancellationToken) => step switch
    {
        Emit emit => EmittedAsync(emit, play, cancellationToken),
        AwaitPermission expected => PermittedAsync(expected, play, cancellationToken),
        AwaitAnswer expected => AnsweredAsync(expected, play, cancellationToken),
        PutFile put => PutAsync(put, play, cancellationToken),
        AwaitInterrupt => InterruptedAsync(cancellationToken),
        Crash crash => Task.FromException<Played>(new InvalidOperationException(crash.Reason)),
        Hangup => Task.FromResult(Played.Hangup),
        Diverge diverge => Task.FromResult(Diverged(play, diverge.Reason)),
        _ => Task.FromResult(Played.Nothing),
    };

    private async Task<Played> EmittedAsync(Emit emit, Play play, CancellationToken cancellationToken) =>
        new([await EmitAsync(emit, play, cancellationToken)], Ends: false);

    private async Task<Played> PutAsync(PutFile put, Play play, CancellationToken cancellationToken)
    {
        await pacing.DelayAsync(play.Timed ? put.Gap : TimeSpan.Zero, cancellationToken);
        await files.WriteAsync(Path.Combine(options.WorkingDirectory, put.Path), put.Content, cancellationToken);

        return Played.Nothing;
    }

    private async Task<Played> InterruptedAsync(CancellationToken cancellationToken)
    {
        AwaitsInterrupt = true;
        await cancellationToken.UntilCancelledAsync();

        return Played.Nothing;
    }

    private async Task<IAgentEvent> EmitAsync(Emit emit, Play play, CancellationToken cancellationToken)
    {
        await pacing.DelayAsync(play.Timed ? emit.Gap : TimeSpan.Zero, cancellationToken);
        var cue = play.Cues.Readdress(emit.Event, play.Token);
        permission = cue is PermissionRequested asked
            ? new Pending<PermissionDecision>(asked.Item, await gates.Permissions.ExpectAsync(asked.Item, cancellationToken))
            : permission;
        form = cue is FormRequested opened
            ? new Pending<FormAnswer>(opened.Item, await gates.Forms.ExpectAsync(opened.Item, cancellationToken))
            : form;
        started |= cue is TurnStarted;

        return cue;
    }

    private async Task<Played> PermittedAsync(AwaitPermission expected, Play play, CancellationToken cancellationToken)
    {
        var divergence = permission?.Item == expected.Decision.Item
            ? Compare(expected.Decision, await gates.Permissions.AwaitAsync(permission.Item, permission.Reply, cancellationToken))
            : Divergence.NeverAsked(expected.Decision.Item);
        permission = null;

        return Settled(play, divergence);
    }

    private async Task<Played> AnsweredAsync(AwaitAnswer expected, Play play, CancellationToken cancellationToken)
    {
        var divergence = form?.Item == expected.Answer.Item
            ? Compare(expected.Answer, await gates.Forms.AwaitAsync(form.Item, form.Reply, cancellationToken))
            : Divergence.NeverAsked(expected.Answer.Item);
        form = null;

        return Settled(play, divergence);
    }

    private Played Settled(Play play, string? divergence) =>
        divergence is null ? Played.Nothing : Diverged(play, divergence);

    private Played Diverged(Play play, string divergence) =>
        new([.. play.Cues.Diverged(divergence, started)], Ends: true);

    private string? Unanswered(IEnumerable<IStep> ahead) =>
        permission is { Reply.IsCompleted: true } && !ahead.Any(step => step is AwaitPermission awaited && awaited.Decision.Item == permission.Item)
            ? Divergence.NeverAnswered(permission.Item)
        : form is { Reply.IsCompleted: true } && !ahead.Any(step => step is AwaitAnswer awaited && awaited.Answer.Item == form.Item)
            ? Divergence.NeverAnswered(form.Item)
        : null;

    private static string? Compare(PermissionDecision recorded, PermissionDecision given) =>
        recorded.Answer == given.Answer && recorded.Message == given.Message ? null : Divergence.Permission(recorded, given);

    private static string? Compare(FormAnswer recorded, FormAnswer given) =>
        Divergence.Same(recorded, given) ? null : Divergence.Answer(recorded, given);

    private async Task WithdrawAsync()
    {
        if (permission is not null)
        {
            await gates.Permissions.WithdrawAsync(permission.Item);
        }

        if (form is not null)
        {
            await gates.Forms.WithdrawAsync(form.Item);
        }
    }

    private sealed record Pending<TReply>(ItemId Item, Task<TReply> Reply);

    private sealed record Play(Cues Cues, bool Timed, ResumeToken Token);

    private sealed record Played(IReadOnlyList<IAgentEvent> Cues, bool Ends)
    {
        public static Played Nothing { get; } = new([], Ends: false);

        public static Played Hangup { get; } = new([], Ends: true);
    }
}
