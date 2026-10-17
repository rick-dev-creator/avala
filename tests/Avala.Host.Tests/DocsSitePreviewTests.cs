using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Resources.Contracts;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class DocsSitePreviewTests(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_DOCFX";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsDocsSiteIsServedByDocfxInItsPreviewWithAvalasC4PlayerAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 with docfx on the PATH to serve a real docs site.");
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("resources.json", $$"""{ {{LeasedPorts.Settings(test: 8)}}, "sampleSeconds": 0.2, "diskSeconds": 60 }""")],
            [(".avala/jobs.json", """{ "docs": { "enabled": true } }""")]);
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-revised] Handle Stripe refund webhooks")));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        var worktree = Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation)).Path;
        var documentation = run.Get<IDocumentation>();
        _ = Outcomes.Succeeds(await documentation.CreateSiteAsync(worktree, Cancellation));
        var site = await documentation.SiteAsync(worktree, Cancellation);
        var available = run.Watch<PreviewAvailable>();
        var sampled = run.Watch<ResourcesSampled>();
        var built = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new FileSystemWatcher(Path.Combine(worktree, site.Root)) { Filter = "manifest.json", IncludeSubdirectories = true };
        watcher.Created += (_, _) => built.TrySetResult();
        watcher.Changed += (_, _) => built.TrySetResult();
        watcher.EnableRaisingEvents = true;

        _ = Outcomes.Succeeds(await run.Get<IPreviews>().RunSiteAsync(job, site.Config, Cancellation));

        await built.Task.WaitAsync(TimeSpan.FromMinutes(2), Cancellation);
        var shown = available.UntilAsync(seen => seen.Target.Job == job);

        while (!shown.IsCompleted)
        {
            run.Clock.Advance(TimeSpan.FromSeconds(0.2));
            _ = await sampled.UntilAsync(_ => true);
        }

        var target = (await shown).Target;
        var slug = Outcomes.Present(documentation.OfJob(job)).Feature.Slug;
        using var http = new HttpClient { BaseAddress = target.Address };
        var page = await http.GetStringAsync(new Uri($"features/{slug}/v2-as-built.html", UriKind.Relative), Cancellation);
        var main = await http.GetStringAsync(new Uri("public/main.js", UriKind.Relative), Cancellation);
        Assert.True(site.CanServe);
        Assert.Contains("```mermaid", await File.ReadAllTextAsync(Path.Combine(worktree, "docs", "features", slug, "v2-as-built.md"), Cancellation));
        Assert.Contains("lang-mermaid", page);
        Assert.Contains("avala-c4", main);
    }
}
