using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class DocumentationTests(PublishedPlugins plugins)
{
    private const string Feedback = "Stripe sends a refund twice; apply it once.";

    private const string DocsOn = """{ "docs": { "enabled": true } }""";

    private const string PlainInstruction = "[simulate: plan-when-asked] Add the changelog";

    private const string PlanBrief = "Avala documents this job's feature from your task list. Before you change anything, record your plan as a task list with your task tools, then keep it updated as you work: mark each task in progress when you start it and completed when it is done, and add or remove tasks when the plan changes.";

    private const string KeptPlanBrief = "Avala documents this job's feature from your task list. Before you act on this message, bring your task list up to date with it: record it again with your task tools if you no longer have it, add, remove or reword the tasks this message changes, and keep marking each task in progress when you start it and completed when it is done.";

    private const string NoPlan = "No plan yet: the agent has not recorded a task list, so there is nothing to document.";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsPlanIsWrittenByADocumentationChildAsItsFeaturesFirstVersionAndItsReviewAsBuiltInTheJobsWorktreeAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-across-turns] Add the changelog")));
        var versions = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        var documented = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));
        var folder = $"docs/features/{documented.Feature.Slug}";
        var worktree = await WorktreeAsync(run, job);
        var writer = Assert.Single(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation));
        Assert.Equal(
            [(1, VersionKind.Proposed, $"{folder}/v1-proposed.md"), (2, VersionKind.AsBuilt, $"{folder}/v2-as-built.md")],
            versions.Where(version => version.Job == job).Select(version => (version.Version.Number, version.Version.Kind, version.Version.Path)));
        Assert.Equal(Option<JobId>.Some(writer.Job), versions[0].Version.Writer);
        Assert.Contains("```mermaid\nC4Container", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "v1-proposed.md"), Cancellation));
        Assert.Equal("# Plan v1\n\n1. Read the code\n2. Write the change\n3. Run the tests\n", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "plans", "v1.md"), Cancellation));
        Assert.Contains("## Evidence\n", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "v2-as-built.md"), Cancellation));
        Assert.Contains("| v2 | As built | [v2-as-built.md](v2-as-built.md) |", await File.ReadAllTextAsync(Path.Combine(worktree, folder, "README.md"), Cancellation));
        Assert.Equal(JobStatus.Discarded, writer.Status);
    }

    [Fact]
    public async Task TheJobsConversationShowsAQuietLineForEachVersionWrittenAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-across-turns] Add the changelog")));
        _ = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "DocsLineViewModel") == 2);

        Assert.Equal(2, await run.Ui.ReadAsync(() => OpenWorkbench.Kinds(conversation).Count(kind => kind == "DocsLineViewModel")));
    }

    [Fact]
    public async Task AnApprovedJobDeliversEveryVersionOfItsFeatureUpToItsLastAsBuiltAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", """{ "docs": { "enabled": true }, "approval": "merge" }"""));
        var (job, _) = await SentBackAsync(run);
        var progressed = run.Watch<JobProgressed>();

        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == job && update.Status == JobStatus.Approved);

        var slug = Outcomes.Present(run.Get<IDocumentation>().OfJob(job)).Feature.Slug;
        var delivered = (await run.Get<IDocumentation>().OfRepositoryAsync(run.Repository.Path, Cancellation)).Features.Single(listed => listed.Feature.Slug == slug);
        Assert.Equal(4, delivered.Versions);
        Assert.Contains("## Evidence\n", (await File.ReadAllTextAsync(Path.Combine(run.Repository.Path, "docs", "features", slug, "v4-as-built.md"), Cancellation)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TheDocumentationCompanionsStayOutOfTheSidebarAndTheDelegationPaneAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-across-turns] Add the changelog")));
        _ = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var header = workbench.Page["Regions"]["Header"].Items;
        var docs = header.Single(item => item.Kind == "DocsToggleViewModel");
        var delegation = header.Single(item => item.Kind == "DelegationToggleViewModel");
        await run.Ui.PresentedAsync(docs.Presentation, () => docs["IsShown"].Value<bool>(), () => "the docs glyph is hidden");
        await run.Ui.PresentedAsync(delegation.Presentation, () => delegation.Presentation.Revision > 0, () => "the delegation glyph was never presented");

        var shown = await run.Ui.ReadAsync(() => (
            SidebarRows.TopLevel(workbench.Sidebar).Single(row => row["Job"].Value<JobId>() == job)["Children"].Items.Count,
            delegation["IsShown"].Value<bool>()));
        Assert.Equal((0, false), shown);
        Assert.Single(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation), child => child.Companion);
    }

    [Fact]
    public async Task AMessageWrittenWhileTheAgentWorksIsRecordedAsASteerAndShownInTheConversationAsync()
    {
        const string Steering = "Keep the old folder name in the changelog.";
        await using var run = await SimulatedRun.StartAsync(plugins, "steer", (".avala/jobs.json", DocsOn));
        var requested = run.Watch<ChangeRequested>();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => conversation["Composer"]["Placeholder"].Text == "Message the agent while it works…");

        await run.Ui.RunAsync(() =>
        {
            conversation["Composer"].Set("Draft", Steering);

            return conversation["Composer"].ExecuteAsync("SendCommand");
        });

        var request = (await requested.UntilAsync(asked => asked.Job == run.Job)).Request;
        Assert.Equal((RequestKind.Steer, Steering, 1), (request.Kind, request.Text, request.Attempt));
        await workbench.ShowsAsync(() => OpenWorkbench.Kinds(conversation).Contains("ChangeLineViewModel"));
    }

    [Fact]
    public async Task ADocumentedJobsAgentIsAskedToRecordItsPlanAsATaskListBeforeItsFirstTurnAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));

        _ = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, PlainInstruction)));

        Assert.Equal($"{PlainInstruction}\n\n{PlanBrief}", await run.ToldAsync());
    }

    [Fact]
    public async Task OnceItsPlanIsDocumentedTheAgentIsAskedToBringItsTaskListUpToDateWithEveryNewMessageAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var activity = run.Watch<AgentActivity>();
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, PlainInstruction)));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, Feedback, Cancellation));

        var told = await activity.UntilAsync(update => update.Event is ItemProgressed { Item.Value: "told" } progressed && progressed.Text.StartsWith(Feedback, StringComparison.Ordinal));
        Assert.Equal($"{Feedback}\n\n{KeptPlanBrief}", ((ItemProgressed)told.Event).Text);
    }

    [Fact]
    public async Task ATaskListTheAgentRevisesAfterASendBackWritesTheFeaturesNextVersionAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, PlainInstruction)));
        var first = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, Feedback, Cancellation));
        var then = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        Assert.Equal(
            [(1, VersionKind.Proposed), (2, VersionKind.AsBuilt), (3, VersionKind.Deviation), (4, VersionKind.AsBuilt)],
            first.Concat(then).Where(version => version.Job == job).Select(version => (version.Version.Number, version.Version.Kind)));
        var folder = Path.Combine(await WorktreeAsync(run, job), "docs", "features", Outcomes.Present(run.Get<IDocumentation>().OfJob(job)).Feature.Slug);
        Assert.Equal("# Plan v3\n\n1. Write the changelog\n2. Link it from the README\n3. Date each entry\n", await File.ReadAllTextAsync(Path.Combine(folder, "plans", "v3.md"), Cancellation));
    }

    [Fact]
    public async Task AvalasNoticeThatItNowAllowsADeniedActionReachesTheAgentWithoutThePlanBriefAsync()
    {
        const string Notice = "Avala now allows web requests to example.com in this job. You may retry it.";
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var activity = run.Watch<AgentActivity>();
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, PlainInstruction)));
        _ = await written.UntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        Outcomes.Succeeds(await run.Get<IJobMessages>().SendBackAsync(job, new JobMessage(Notice) { Author = MessageAuthor.Avala }, Cancellation));

        var told = await activity.UntilAsync(update => update.Event is ItemProgressed { Item.Value: "told" } progressed && progressed.Text.StartsWith(Notice, StringComparison.Ordinal));
        Assert.Equal(Notice, ((ItemProgressed)told.Event).Text);
    }

    [Fact]
    public async Task AJobWithoutDocsIsNotAskedForATaskListAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);

        _ = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, PlainInstruction)));

        Assert.Equal(PlainInstruction, await run.ToldAsync());
    }

    [Fact]
    public async Task APlainInstructionThatNamesNoPlanStillGetsAProposedAndAnAsBuiltVersionAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var written = run.Watch<VersionWritten>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, PlainInstruction)));
        var versions = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        Assert.Equal([VersionKind.Proposed, VersionKind.AsBuilt], versions.Where(version => version.Job == job).Select(version => version.Version.Kind));
    }

    [Fact]
    public async Task ATurnThatEndsWithoutAPlanMakesTheDocsPaneSayWhyItIsEmptyAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("mentions"))));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        var contributions = run.Get<IEnumerable<RegionContribution>>().ToList();
        var toggle = new Bound(contributions.Single(contribution => contribution.Region == WorkbenchRegions.JobHeader && contribution.ViewModel.GetType().Name == "DocsToggleViewModel").ViewModel);
        var pane = new Bound(contributions.Single(contribution => contribution.Region == WorkbenchRegions.Preview && contribution.ViewModel.GetType().Name == "DocsPaneViewModel").ViewModel);
        await run.Ui.PresentedAsync(toggle.Presentation, () => toggle["IsShown"].Value<bool>(), () => "the docs glyph is not shown");

        await run.Ui.InvokeAsync(() => toggle.Execute("ToggleCommand"), Cancellation);

        await run.Ui.PresentedAsync(pane.Presentation, () => pane["IsOpen"].Value<bool>() && pane["State"].Text == NoPlan, () => $"the pane says {pane["State"].Text}");
        Assert.Empty(await run.Get<IJobCatalog>().ChildrenAsync(job, Cancellation));
    }

    [Fact]
    public async Task WithoutTheDocsSectionAJobsPlanStartsNoDocumentationChildAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns");

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.DeliveredAsync();

        Assert.Empty(await run.Get<IJobCatalog>().ChildrenAsync(run.Job, Cancellation));
        Assert.Equal(Option<JobDocumentation>.None, run.Get<IDocumentation>().OfJob(run.Job));
    }

    [Fact]
    public async Task ASendBackThatChangesThePlanIsRecordedAsAChangeRequestThatTheDeviationWrittenNextLinksInChangesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));

        var (job, versions) = await SentBackAsync(run);

        var documented = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));
        var folder = Path.Combine(await WorktreeAsync(run, job), "docs", "features", documented.Feature.Slug);
        Assert.Equal(
            [(1, VersionKind.Proposed), (2, VersionKind.AsBuilt), (3, VersionKind.Deviation), (4, VersionKind.AsBuilt)],
            versions.Select(version => (version.Number, version.Kind)));
        var request = Assert.Single(documented.Requests);
        Assert.Equal((RequestKind.SendBack, Feedback, 2, Option<int>.Some(3)), (request.Kind, request.Text, request.Attempt, request.Version));
        var changes = await File.ReadAllTextAsync(Path.Combine(folder, "changes.md"), Cancellation);
        Assert.Contains($"> {Feedback}\n", changes);
        Assert.Contains("v3-deviation.md", changes);
        Assert.Contains("1. Add a Webhooks container\n2. Keep idempotency keys in the ledger\n", await File.ReadAllTextAsync(Path.Combine(folder, "plans", "v3.md"), Cancellation));
    }

    [Fact]
    public async Task ADeviationThatShapesTheStructureGetsANumberedDecisionRecordLinkedBothWaysWithItsVersionAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));

        var (job, _) = await SentBackAsync(run);

        var slug = Outcomes.Present(run.Get<IDocumentation>().OfJob(job)).Feature.Slug;
        var docs = Path.Combine(await WorktreeAsync(run, job), "docs");
        var record = Assert.Single(Directory.GetFiles(Path.Combine(docs, "architecture", "decisions"), "0*.md"));
        Assert.Equal("0001-keep-idempotency-keys-in-the-ledger.md", Path.GetFileName(record));
        Assert.Contains($"features/{slug}/v3-deviation.md", (await File.ReadAllTextAsync(record, Cancellation)).ReplaceLineEndings("\n"));
        Assert.Contains("architecture/decisions/0001-keep-idempotency-keys-in-the-ledger.md", await File.ReadAllTextAsync(Path.Combine(docs, "features", slug, "v3-deviation.md"), Cancellation));
    }

    [Fact]
    public async Task AJobsVersionsAndChangeRequestsSurviveARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var (job, _) = await SentBackAsync(run);
        var before = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));

        await run.RestartAsync();
        await run.StartedAsync();

        var after = Outcomes.Present(run.Get<IDocumentation>().OfJob(job));
        Assert.Equal(before.Feature, after.Feature);
        Assert.Equal(before.Versions, after.Versions);
        Assert.Equal(before.Requests, after.Requests);
        Assert.Equal(Option<JobId>.None, after.Writing);
        var docs = Outcomes.Present(await run.Get<IDocumentation>().JobDocsAsync(job, Cancellation));
        Assert.Equal(4, Outcomes.Present(docs.Story).Versions.Count);
    }

    [Fact]

    [UsesRealGit]
    public async Task CreatingTheSiteFromSettingsWritesNothingInTheWorkingCopyAndShowsTheJobThatPreparesItLinkedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var workbench = await run.WorkbenchAsync();
        var docs = await DocsSettingAsync(run);

        await run.Ui.RunAsync(() => docs.ExecuteAsync("CreateSiteCommand"));

        Assert.False(Directory.Exists(Path.Combine(run.Repository.Path, "docs")));
        Assert.Equal(string.Empty, (await run.Repository.GitAsync(Cancellation, "status", "--porcelain")).Trim());
        var job = Outcomes.Present((await run.Get<IDocumentation>().SiteAsync(run.Repository.Path, Cancellation)).Preparing);
        Assert.Contains("docs/get-started/quickstart.md", Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Instruction);
        Assert.Equal(
            (true, false, "Being prepared by its job. Review it like any job: the site is here once it is merged."),
            await run.Ui.ReadAsync(() => (docs["IsSitePreparing"].Value<bool>(), docs["CanCreateSite"].Value<bool>(), docs["Site"].Text)));
        await run.Ui.InvokeAsync(() => docs.Execute("OpenSiteJobCommand"), Cancellation);
        await workbench.ShowsAsync(() => workbench.Page.Has("Conversation") && workbench.Page["Conversation"]["Job"].Value<JobId>() == job);
    }

    [Fact]

    [UsesRealGit]
    public async Task TheSiteJobDeliveredByMergeBringsTheSiteWithAvalasPlayerIntoTheBaseBranchAndSettingsShowsItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", """{ "docs": { "enabled": true }, "approval": "merge" }"""));
        var documentation = run.Get<IDocumentation>();
        var job = Outcomes.Succeeds(await documentation.CreateSiteAsync(run.Repository.Path, Cancellation)).Job;
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));

        Assert.Contains("\"template\": [ \"default\", \"modern\", \"template\" ]", await run.Repository.GitAsync(Cancellation, "show", "main:docs/docfx.json"));
        Assert.Contains("customElements.define(\"avala-c4\"", await run.Repository.GitAsync(Cancellation, "show", "main:docs/template/public/avala-c4.js"));
        var site = await documentation.SiteAsync(run.Repository.Path, Cancellation);
        Assert.Equal((true, Option<JobId>.None), (site.Exists, site.Preparing));
        var docs = await DocsSettingAsync(run);
        Assert.Equal((false, false), await run.Ui.ReadAsync(() => (docs["IsSitePreparing"].Value<bool>(), docs["CanCreateSite"].Value<bool>())));
    }

    [Fact]
    public async Task TheSiteJobIsNotDocumentedAsAFeatureAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));

        var job = Outcomes.Succeeds(await run.Get<IDocumentation>().CreateSiteAsync(run.Repository.Path, Cancellation)).Job;

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        Assert.Equal(Option<JobDocumentation>.None, run.Get<IDocumentation>().OfJob(job));
    }

    [Fact]
    public async Task ADiscardedSiteJobLeavesNoSiteAndSettingsOffersCreateSiteAgainAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/jobs.json", DocsOn));
        var documentation = run.Get<IDocumentation>();
        var job = Outcomes.Succeeds(await documentation.CreateSiteAsync(run.Repository.Path, Cancellation)).Job;
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        _ = Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(job, Cancellation));

        var site = await documentation.SiteAsync(run.Repository.Path, Cancellation);
        Assert.Equal((false, Option<JobId>.None), (site.Exists, site.Preparing));
        var docs = await DocsSettingAsync(run);
        Assert.Equal((false, true), await run.Ui.ReadAsync(() => (docs["IsSitePreparing"].Value<bool>(), docs["CanCreateSite"].Value<bool>())));
    }

    private static async Task<Bound> DocsSettingAsync(SimulatedRun run)
    {
        var settings = (await Pages.ActivatedAsync(run, "Settings"))["Repository"];
        await run.Ui.RunAsync(() => settings.ExecuteAsync("OpenCommand", run.Repository.Path));

        return await run.Ui.ReadAsync(() => settings["Docs"]);
    }

    private static async Task<(JobId Job, IReadOnlyList<FeatureVersion> Versions)> SentBackAsync(SimulatedRun run)
    {
        var written = run.Watch<VersionWritten>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: plan-revised] Handle Stripe refund webhooks")));
        var first = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);
        Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, Feedback, Cancellation));
        var then = await written.CollectUntilAsync(version => version.Job == job && version.Version.Kind == VersionKind.AsBuilt);

        return (job, [.. first.Concat(then).Where(version => version.Job == job).Select(version => version.Version)]);
    }

    private static async Task<string> WorktreeAsync(SimulatedRun run, JobId job)
    {
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));

        return Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(Outcomes.Present(history.Summary.Workspace), Cancellation)).Path;
    }
}
