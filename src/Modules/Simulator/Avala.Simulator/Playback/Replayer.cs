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
            foreach (var step in conversation.NextScript)
            {
                var played = await StepAsync(step, play, cancellationToken);

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

    private async Task<Played> StepAsync(IStep step, Play play, CancellationToken cancellationToken)
    {
        if (Unanswered(step) is { } unanswered)
        {
            return Diverged(play, unanswered);
        }

        switch (step)
        {
            case Emit emit:
                return new Played([await EmitAsync(emit, play, cancellationToken)], Ends: false);
            case AwaitPermission expected:
                return Settled(play, await PermittedAsync(expected, cancellationToken));
            case AwaitAnswer expected:
                return Settled(play, await AnsweredAsync(expected, cancellationToken));
            case PutFile put:
                await pacing.DelayAsync(play.Timed ? put.Gap : TimeSpan.Zero, cancellationToken);
                await files.WriteAsync(Path.Combine(options.WorkingDirectory, put.Path), put.Content, cancellationToken);

                return Played.Nothing;
            case AwaitInterrupt:
                AwaitsInterrupt = true;
                await cancellationToken.UntilCancelledAsync();

                return Played.Nothing;
            case Crash crash:
                throw new InvalidOperationException(crash.Reason);
            case Hangup:
                return Played.Hangup;
            case Diverge diverge:
                return Diverged(play, diverge.Reason);
            default:
                return Played.Nothing;
        }
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

    private async Task<string?> PermittedAsync(AwaitPermission expected, CancellationToken cancellationToken)
    {
        var divergence = permission?.Item == expected.Decision.Item
            ? Compare(expected.Decision, await gates.Permissions.AwaitAsync(permission.Item, permission.Reply, cancellationToken))
            : Divergence.NeverAsked(expected.Decision.Item);
        permission = null;

        return divergence;
    }

    private async Task<string?> AnsweredAsync(AwaitAnswer expected, CancellationToken cancellationToken)
    {
        var divergence = form?.Item == expected.Answer.Item
            ? Compare(expected.Answer, await gates.Forms.AwaitAsync(form.Item, form.Reply, cancellationToken))
            : Divergence.NeverAsked(expected.Answer.Item);
        form = null;

        return divergence;
    }

    private Played Settled(Play play, string? divergence) =>
        divergence is null ? Played.Nothing : Diverged(play, divergence);

    private Played Diverged(Play play, string divergence) =>
        new([.. play.Cues.Diverged(divergence, started)], Ends: true);

    private string? Unanswered(IStep step) =>
        permission is { Reply.IsCompleted: true } && step is not AwaitPermission ? Divergence.NeverAnswered(permission.Item)
        : form is { Reply.IsCompleted: true } && step is not AwaitAnswer ? Divergence.NeverAnswered(form.Item)
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
