using System.Runtime.CompilerServices;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal sealed class Replayer(SessionOptions options, IFileWriter files, Gates gates, Pacing pacing)
{
    public bool Closing { get; private set; }

    public bool AwaitsInterrupt { get; private set; }

    public async IAsyncEnumerable<IAgentEvent> PlayAsync(
        Cues cues,
        Conversation conversation,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var timed = conversation.Scenario.AsRecorded;
        var token = conversation.Advanced.Token;
        var started = false;
        Pending<PermissionDecision>? permission = null;
        Pending<FormAnswer>? form = null;
        AwaitsInterrupt = false;

        try
        {
            foreach (var step in conversation.NextScript)
            {
                var divergence = Unanswered(step, permission, form);

                switch (step)
                {
                    case Emit emit when divergence is null:
                        await pacing.DelayAsync(timed ? emit.Gap : TimeSpan.Zero, cancellationToken);
                        var cue = cues.Readdress(emit.Event, token);
                        permission = cue is PermissionRequested asked
                            ? new Pending<PermissionDecision>(asked.Item, await gates.Permissions.ExpectAsync(asked.Item, cancellationToken))
                            : permission;
                        form = cue is FormRequested opened
                            ? new Pending<FormAnswer>(opened.Item, await gates.Forms.ExpectAsync(opened.Item, cancellationToken))
                            : form;
                        started |= cue is TurnStarted;
                        yield return cue;
                        break;
                    case AwaitPermission expected when divergence is null:
                        divergence = permission?.Item == expected.Decision.Item
                            ? Compare(expected.Decision, await gates.Permissions.AwaitAsync(permission.Item, permission.Reply, cancellationToken))
                            : Divergence.NeverAsked(expected.Decision.Item);
                        permission = null;
                        break;
                    case AwaitAnswer expected when divergence is null:
                        divergence = form?.Item == expected.Answer.Item
                            ? Compare(expected.Answer, await gates.Forms.AwaitAsync(form.Item, form.Reply, cancellationToken))
                            : Divergence.NeverAsked(expected.Answer.Item);
                        form = null;
                        break;
                    case PutFile put when divergence is null:
                        await pacing.DelayAsync(timed ? put.Gap : TimeSpan.Zero, cancellationToken);
                        await files.WriteAsync(Path.Combine(options.WorkingDirectory, put.Path), put.Content, cancellationToken);
                        break;
                    case AwaitInterrupt when divergence is null:
                        AwaitsInterrupt = true;
                        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                        break;
                    case Crash crash when divergence is null:
                        throw new InvalidOperationException(crash.Reason);
                    case Hangup when divergence is null:
                        Closing = true;
                        yield break;
                    case Diverge diverge:
                        divergence ??= diverge.Reason;
                        break;
                }

                if (divergence is not null)
                {
                    Closing = true;

                    foreach (var cue in cues.Diverged(divergence, started))
                    {
                        yield return cue;
                    }

                    yield break;
                }
            }
        }
        finally
        {
            await WithdrawAsync(permission, form);
        }
    }

    private static string? Unanswered(IStep step, Pending<PermissionDecision>? permission, Pending<FormAnswer>? form) =>
        permission is { Reply.IsCompleted: true } && step is not AwaitPermission ? Divergence.NeverAnswered(permission.Item)
        : form is { Reply.IsCompleted: true } && step is not AwaitAnswer ? Divergence.NeverAnswered(form.Item)
        : null;

    private static string? Compare(PermissionDecision recorded, PermissionDecision given) =>
        recorded.Answer == given.Answer && recorded.Message == given.Message ? null : Divergence.Permission(recorded, given);

    private static string? Compare(FormAnswer recorded, FormAnswer given) =>
        Divergence.Same(recorded, given) ? null : Divergence.Answer(recorded, given);

    private async Task WithdrawAsync(Pending<PermissionDecision>? permission, Pending<FormAnswer>? form)
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
}
