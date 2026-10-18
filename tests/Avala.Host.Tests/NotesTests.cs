using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class NotesTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANoteTakenWhileWatchingAJobIsAFolderInItsWorktreeWithItsImageBesideItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await run.SettledAsync(job);
        var summary = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary;
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(summary.Workspace), Cancellation)).Path;
        var screen = Path.Combine(Directory.CreateTempSubdirectory().FullName, "login.png");
        await File.WriteAllBytesAsync(screen, Pngs.Of(2, 2), Cancellation);
        var notes = run.Get<INotes>();

        var taken = Outcomes.Succeeds(await notes.TakeAsync(NotePlace.OfJob(job, summary.Repository), new NoteDraft("Login bug", "The button overlaps."), Cancellation));
        _ = Outcomes.Succeeds(await notes.AttachAsync(taken.Id, screen, "The login screen", Cancellation));

        var folder = Path.Combine(worktree, taken.Folder);
        Assert.StartsWith($".avala/notes/{job.Value:N}/", taken.Folder, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder, "login.png")));
        Assert.Contains("![The login screen](login.png)", await File.ReadAllTextAsync(Path.Combine(folder, "note.md"), Cancellation), StringComparison.Ordinal);
        var listed = Assert.Single(await notes.ListAsync(NotePlace.OfJob(job, summary.Repository), Cancellation));
        Assert.Equal("Login bug", listed.Title);
        Assert.Equal(["login.png"], listed.Media.Select(media => media.File));
    }
}
