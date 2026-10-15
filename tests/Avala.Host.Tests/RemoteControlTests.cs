using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class RemoteControlTests(PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] Login =
    [
        ("connections.json", """{ "default": "claude", "connections": [ { "name": "claude", "provider": "simulator", "credential": { "source": "login" } } ] }"""),
        ("connections/claude/.login", string.Empty),
    ];

    [Fact]
    public async Task AJobContinuedFromThePhoneShowsTheMessageAsSentFromThePhoneAndAsksForReviewAgainAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Login, []);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("remote-control"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        var remote = workbench.Page["Regions"]["Header"].Items.Single(item => item.Kind == "RemoteControlViewModel");
        await run.Ui.PresentedAsync(remote.Presentation, () => remote["IsShown"].Value<bool>(), () => remote["Status"].Text);
        var offered = await run.Ui.ReadAsync(() => (remote["IsOn"].Value<bool>(), remote["Status"].Text));

        await run.Ui.RunAsync(() => remote.ExecuteAsync("ToggleCommand"));

        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("Partial refunds are covered by the tests now."));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        await run.Ui.PresentedAsync(remote.Presentation, () => remote["IsConnected"].Value<bool>(), () => remote["Status"].Text);
        var prompts = await run.Ui.ReadAsync(() => conversation["Entries"].Items.Where(entry => entry.Kind == "PromptViewModel").Select(prompt => (prompt["Origin"].Text, prompt["Text"].Text)).ToList());
        var link = await run.Ui.ReadAsync(() => remote["Link"].Text);

        Assert.Equal((false, "Follow and continue this job from the Claude app or any browser."), offered);
        Assert.Equal(("From your phone", "Also cover partial refunds"), prompts[^1]);
        Assert.StartsWith("https://remote.simulator.invalid/session/", link, StringComparison.Ordinal);
    }
}
