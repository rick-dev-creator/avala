using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Notifications.Contracts;
using Avala.Permissions.Contracts;
using Avala.Runtime.Presence;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class NotificationTests(PublishedPlugins plugins)
{
    private const string Topic = "avala-simulated";

    private static readonly (string File, string Content)[] Settings = [("notifications.json", $$"""{ "ntfy": { "topic": "{{Topic}}" } }""")];

    private static readonly TimeSpan Away = TimeSpan.FromMinutes(3);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APermissionLeftToAnAbsentPersonSendsExactlyOneMinimalNtfyMessageAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("permission"), Settings, []);
        var settled = run.Watch<NotificationSettled>();
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        await run.DeliveredAsync();

        run.Clock.Advance(Away);

        var sent = (await settled.UntilAsync(_ => true)).Record;
        var request = Assert.Single(run.Notifier.Received);
        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal((NotificationKind.Decision, NotificationOutcome.Sent, run.Job), (sent.Kind, sent.Outcome, Outcomes.Present(sent.Job)));
        Assert.Equal(("POST", "https://ntfy.sh/"), (request.Method, request.Address.AbsoluteUri));
        Assert.Equal(
            (Topic, $"A decision needs you · {Path.GetFileName(run.Repository.Path)}", SimulatedRun.Simulate("permission")),
            (body.RootElement.GetProperty("topic").GetString(), body.RootElement.GetProperty("title").GetString(), body.RootElement.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task APersonAtTheWindowGetsNothingWhileTheyAnswerInTheAppAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("permission"), Settings, []);
        var settled = run.Watch<NotificationSettled>();
        var asked = await run.DecisionAsync();
        run.Get<WindowPresence>().Seen();
        await run.DeliveredAsync();

        run.Clock.Advance(TimeSpan.FromSeconds(90));
        run.Get<WindowPresence>().Seen();
        Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(asked.Session, new PermissionReply(asked.Item, PermissionAnswer.Allow), Cancellation));

        Assert.Equal(NotificationOutcome.Withdrawn, (await settled.UntilAsync(_ => true)).Record.Outcome);
        Assert.Empty(run.Notifier.Received);
    }

    [Fact]
    public async Task ADecisionAnsweredInTheAppWithinTheCoalescingWindowIsNeverSentAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("permission"), Settings, []);
        var settled = run.Watch<NotificationSettled>();
        var asked = await run.DecisionAsync();

        Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(asked.Session, new PermissionReply(asked.Item, PermissionAnswer.Allow), Cancellation));
        var withdrawn = (await settled.UntilAsync(_ => true)).Record;
        run.Clock.Advance(Away);
        await run.DeliveredAsync();

        Assert.Equal(NotificationOutcome.Withdrawn, withdrawn.Outcome);
        Assert.Empty(run.Notifier.Received);
    }
}
