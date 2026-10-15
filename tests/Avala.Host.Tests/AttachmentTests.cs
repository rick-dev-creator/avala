using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class AttachmentTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobStartedWithAnAttachmentHandsItToTheAgentAndShowsItUnderItsPromptAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        using var picked = new TemporaryFolder();
        var screenshot = Path.Combine(picked.Path, "screen.png");
        await File.WriteAllBytesAsync(screenshot, [137, 80, 78, 71], Cancellation);
        var attached = Outcomes.Succeeds(await run.Get<IJobAttachments>().KeepFileAsync(screenshot, Cancellation));

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("attachments")) { Attachments = [attached] }));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);

        await workbench.ShowsAsync(() => OpenWorkbench.Texts(conversation, "MessageViewModel", "Text").Contains("I received screen.png (image/png)."));
        var prompt = OpenWorkbench.Entry(conversation, "PromptViewModel")!.Value;
        await workbench.ShowsAsync(() => prompt["HasAttachments"].Value<bool>());
        Assert.Equal(["screen.png"], await run.Ui.ReadAsync(() => prompt["Attachments"].Items.Select(chip => chip["Name"].Text).ToList()));
        Assert.NotEqual(screenshot, attached.Path);
    }
}
