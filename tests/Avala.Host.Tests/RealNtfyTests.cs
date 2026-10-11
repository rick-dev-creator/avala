using Avala.Agents.Contracts.Events;
using Avala.Notifications.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class RealNtfyTests(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_NTFY_TOPIC";

    private static readonly TimeSpan Away = TimeSpan.FromMinutes(3);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Topic => Environment.GetEnvironmentVariable(Gate) ?? string.Empty;

    private static string Server => Environment.GetEnvironmentVariable("AVALA_REAL_NTFY_SERVER") is { Length: > 0 } server
        ? server
        : NtfyTarget.DefaultServer.AbsoluteUri;

    private static (string File, string Content)[] Settings =>
        [("notifications.json", $$"""{ "ntfy": { "server": "{{Server}}", "topic": "{{Topic}}" } }""")];

    [Fact]
    public async Task TheTestMessageReachesTheRealNtfyTopicAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, Settings, [], realNotifier: true);

        var sent = Outcomes.Succeeds(await run.Get<INotifications>().SendTestAsync(Cancellation));

        Assert.Equal((NotificationKind.Test, NotificationOutcome.Sent, 1), (sent.Kind, sent.Outcome, sent.Attempts));
    }

    [Fact]
    public async Task APermissionLeftToAnAbsentPersonReachesTheRealNtfyTopicOnceAsync()
    {
        Gated();
        await using var run = await SimulatedRun.RealNotifierAsync(plugins, "[simulate: permission] Real ntfy check: decision", Settings);
        var settled = run.Watch<NotificationSettled>();
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        await run.DeliveredAsync();

        run.Clock.Advance(Away);

        var sent = (await settled.UntilAsync(_ => true)).Record;
        Assert.Equal((NotificationKind.Decision, NotificationOutcome.Sent, 1, run.Job), (sent.Kind, sent.Outcome, sent.Attempts, Outcomes.Present(sent.Job)));
        Assert.Equal(sent, Assert.Single(run.Get<INotifications>().Recent()));
    }

    [Fact]
    public async Task ADecisionAnsweredInTheAppWithinTheCoalescingWindowNeverReachesTheRealNtfyTopicAsync()
    {
        Gated();
        await using var run = await SimulatedRun.RealNotifierAsync(plugins, "[simulate: permission] Real ntfy check: answered in the app", Settings);
        var settled = run.Watch<NotificationSettled>();
        var asked = await run.DecisionAsync();

        Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(asked.Session, new PermissionReply(asked.Item, PermissionAnswer.Allow), Cancellation));
        var withdrawn = (await settled.UntilAsync(_ => true)).Record;
        run.Clock.Advance(Away);
        await run.DeliveredAsync();

        Assert.Equal(NotificationOutcome.Withdrawn, withdrawn.Outcome);
        Assert.Equal(withdrawn, Assert.Single(run.Get<INotifications>().Recent()));
    }

    private static void Gated() =>
        Assert.SkipUnless(Topic.Length > 0, $"Set {Gate} to a ntfy topic to send real phone notifications.");
}
