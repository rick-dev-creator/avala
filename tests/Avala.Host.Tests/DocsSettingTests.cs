using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class DocsSettingTests(PublishedPlugins plugins)
{
    private const string OnlyInTheWorkingCopy = "On in your working copy only: new jobs run without docs until .avala/jobs.json is committed.";

    private const string Approval = """{ "approval": "keep" }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DocsTurnedOnStayOnWhenSettingsIsShownAgainAndSayTheyAreOnlyInTheWorkingCopyUntilCommittedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", Approval));
        var docs = await DocsOfAsync(run);
        await run.Ui.RunAsync(() => docs.ExecuteAsync("SwitchCommand"));

        var shownAgain = await DocsOfAsync(run);

        Assert.Equal(
            (true, true, OnlyInTheWorkingCopy, true),
            await run.Ui.ReadAsync(() => (shownAgain["IsOn"].Value<bool>(), shownAgain["IsUncommitted"].Value<bool>(), shownAgain["Note"].Text, shownAgain.CanExecute("CommitCommand"))));
    }

    [Fact]
    public async Task CommittingTheJobFileFromSettingsCommitsItAloneOnTheBranchAndTheNextJobIsAskedForItsPlanAndDocumentedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", Approval));
        await run.Repository.GitAsync(Cancellation, "config", "user.name", "Ada");
        await run.Repository.GitAsync(Cancellation, "config", "user.email", "ada@localhost");
        await File.WriteAllTextAsync(Path.Combine(run.Repository.Path, "staged.txt"), "staged by the person\n", Cancellation);
        await run.Repository.GitAsync(Cancellation, "add", "staged.txt");
        var docs = await DocsOfAsync(run);
        await run.Ui.RunAsync(() => docs.ExecuteAsync("SwitchCommand"));

        await run.Ui.RunAsync(() => docs.ExecuteAsync("CommitCommand"));

        Assert.Equal(string.Empty, await run.Ui.ReadAsync(() => docs["Error"].Text));
        var committed = (await run.Repository.GitAsync(Cancellation, "show", "--name-only", "--format=%s", "HEAD")).Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["Turn on feature documentation", ".avala/jobs.json"], committed);
        Assert.Equal("staged.txt", (await run.Repository.GitAsync(Cancellation, "diff", "--cached", "--name-only")).Trim());
        Assert.Contains("\"enabled\": true", await run.Repository.GitAsync(Cancellation, "show", "HEAD:.avala/jobs.json"), StringComparison.Ordinal);
        var shown = await run.Ui.ReadAsync(() => (docs["IsOn"].Value<bool>(), docs["IsUncommitted"].Value<bool>(), docs["Note"].Text, docs["Error"].Text));
        Assert.Equal((true, false, string.Empty), (shown.Item1, shown.Item2, shown.Item4));
        Assert.Matches("^Committed \\.avala/jobs\\.json as [0-9a-f]{7} on main\\. New jobs follow it\\.$", shown.Item3);

        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-when-asked] Add the changelog")));
        Assert.Equal(VersionKind.Proposed, (await written.UntilAsync(version => version.Job == job)).Version.Kind);
    }

    [Fact]
    public async Task OnACheckoutWithoutABranchCommittingTheJobFileCommitsNothingAndSaysWhyAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", Approval));
        await run.Repository.GitAsync(Cancellation, "checkout", "--quiet", "--detach");
        var before = await run.Repository.GitAsync(Cancellation, "rev-parse", "HEAD");
        var docs = await DocsOfAsync(run);
        await run.Ui.RunAsync(() => docs.ExecuteAsync("SwitchCommand"));

        await run.Ui.RunAsync(() => docs.ExecuteAsync("CommitCommand"));

        Assert.Equal(before, await run.Repository.GitAsync(Cancellation, "rev-parse", "HEAD"));
        Assert.Equal(
            (true, "Not committed: the repository is not on a branch. Check out a branch, then commit again."),
            await run.Ui.ReadAsync(() => (docs["IsUncommitted"].Value<bool>(), docs["Error"].Text)));
    }

    private static async Task<Bound> DocsOfAsync(SimulatedRun run)
    {
        var settings = (await Pages.ActivatedAsync(run, "Settings"))["Repository"];
        await run.Ui.RunAsync(() => settings.ExecuteAsync("OpenCommand", run.Repository.Path));

        return await run.Ui.ReadAsync(() => settings["Docs"]);
    }
}
