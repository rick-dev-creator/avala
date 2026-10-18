using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class NotesPaneTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANoteWrittenInASettledJobsNotesPaneIsListedThereAndKeptInItsWorktreeAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await run.SettledAsync(job);
        var summary = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary;
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(summary.Workspace), Cancellation)).Path;
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var toggle = workbench.Page["Regions"]["Header"].Items.Single(item => item.Kind == "NotesToggleViewModel");
        var pane = workbench.Page["Regions"]["Preview"].Items.Single(item => item.Kind == "NotesPaneViewModel");

        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), Cancellation);
        await run.Ui.PresentedAsync(pane.Presentation, () => pane["Board"]["Place"].Value<Option<NotePlace>>().IsSome, () => "no place");
        await run.Ui.InvokeAsync(
            () =>
            {
                pane["Board"].Execute("NewCommand");
                pane["Board"].Set("Title", "Login bug");
                pane["Board"].Set("Body", "The button overlaps the logo.");
            },
            Cancellation);
        await run.Ui.RunAsync(() => pane["Board"].ExecuteAsync("SaveCommand"));

        var listed = await run.Ui.ReadAsync(() => (
            pane["IsOpen"].Value<bool>(),
            toggle["IsOpen"].Value<bool>(),
            pane["Board"]["Notes"].Items.Select(note => (note["Title"].Text, note["Excerpt"].Text)).ToList()));
        var kept = Assert.Single(await run.Get<INotes>().ListAsync(NotePlace.OfJob(job, summary.Repository), Cancellation));
        Assert.Equal((true, true), (listed.Item1, listed.Item2));
        Assert.Equal([("Login bug", "The button overlaps the logo.")], listed.Item3);
        Assert.StartsWith($".avala/notes/{job.Value:N}/", kept.Folder, StringComparison.Ordinal);
        Assert.Contains("The button overlaps the logo.", await File.ReadAllTextAsync(Path.Combine(worktree, kept.Folder, "note.md"), Cancellation), StringComparison.Ordinal);
    }
}
