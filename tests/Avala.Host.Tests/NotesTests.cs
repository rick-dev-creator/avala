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

    [Fact]
    public async Task ANoteTakenOnASettledJobIsCommittedToItsBranchAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await run.SettledAsync(job);
        var summary = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary;
        var workspace = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(summary.Workspace), Cancellation));

        var taken = Outcomes.Succeeds(await run.Get<INotes>().TakeAsync(NotePlace.OfJob(job, summary.Repository), new NoteDraft("Login bug", "The button overlaps."), Cancellation));
        await run.DeliveredAsync();

        Assert.Equal("Note: Login bug", (await run.Repository.GitAsync(Cancellation, "log", "-1", "--format=%s", workspace.Branch)).Trim());
        Assert.Contains($"{taken.Folder}/note.md", await run.Repository.GitAsync(Cancellation, "show", "--name-only", "--format=", workspace.Branch), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANoteTakenOutsideAJobIsCommittedToTheProjectAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await run.SettledAsync(job);
        var repository = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Repository;

        var taken = Outcomes.Succeeds(await run.Get<INotes>().TakeAsync(NotePlace.OfProject(repository), new NoteDraft("Roadmap", "Ship notes."), Cancellation));
        await run.DeliveredAsync();

        Assert.StartsWith(".avala/notes/project/", taken.Folder, StringComparison.Ordinal);
        Assert.Equal("Note: Roadmap", (await run.Repository.GitAsync(Cancellation, "log", "-1", "--format=%s")).Trim());
    }

    [Fact]
    public async Task ADiscardedJobsNotesAreKeptInTheProjectAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [(".avala/permissions.json", """{ "autonomy": "autonomous" }""")]);
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("edit"))));
        _ = await run.SettledAsync(job);
        var repository = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Repository;
        var taken = Outcomes.Succeeds(await run.Get<INotes>().TakeAsync(NotePlace.OfJob(job, repository), new NoteDraft("Login bug", "The button overlaps."), Cancellation));
        await run.DeliveredAsync();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(job, Cancellation));
        await run.DeliveredAsync();

        Assert.True(File.Exists(Path.Combine(repository, taken.Folder, "note.md")));
        Assert.Equal($"Keep the notes of discarded job {job.Value:N}", (await run.Repository.GitAsync(Cancellation, "log", "-1", "--format=%s")).Trim());
    }
}
