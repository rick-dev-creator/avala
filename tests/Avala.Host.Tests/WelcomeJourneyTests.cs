using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class WelcomeJourneyTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AChatStartedFromTheWelcomeRunsInAScratchRepositoryUntilItsReviewAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var workbench = await run.WorkbenchAsync();
        var welcome = await run.Ui.ReadAsync(() => workbench.Page["Welcome"]);
        await run.Ui.RunAsync(() => workbench.Page["Checking"].Value<Task>());

        await run.Ui.RunAsync(() =>
        {
            welcome.Set("Instruction", SimulatedRun.Simulate("reply"));

            return welcome.ExecuteAsync("StartCommand");
        });
        await workbench.ShowsAsync(() => workbench.Page.Has("Conversation"));
        var job = await run.Ui.ReadAsync(() => workbench.Page["Conversation"]["Job"].Value<JobId>());

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var summary = (await run.Get<IJobCatalog>().ListAsync(Cancellation)).Single(found => found.Job == job);
        Assert.True(run.Get<IScratchRepositories>().Holds(summary.Repository));
        await workbench.ShowsAsync(() => welcome["IsCompact"].Value<bool>());
    }

    [Fact]
    public async Task ARepositoryClonedFromTheWelcomesListRunsAJobThatIsApprovedAsync()
    {
        await using var projects = new TemporaryFolder();
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = Outcomes.Succeeds(await run.Get<IRepositoryPublishing>().PublishAsync(run.Repository.Path, new ForgeName("local"), new RepositoryDraft("shop", Private: true), Cancellation));
        _ = Outcomes.Succeeds(await run.Get<IProjectsFolder>().ChooseAsync(projects.Path, Cancellation));
        var workbench = await run.WorkbenchAsync();
        var welcome = await run.Ui.ReadAsync(() => workbench.Page["Welcome"]);
        var page = run.Page("New job");

        await run.Ui.RunAsync(() => welcome.ExecuteAsync("CloneCommand"));
        var sheet = await run.Ui.ReadAsync(() => page["Chooser"]["Clone"]);
        await run.Ui.RunAsync(() => sheet["Loading"].Value<Task>());
        var listed = await run.Ui.ReadAsync(() => sheet["Repositories"].Items.Select(row => row["Name"].Text).ToList());
        await run.Ui.RunAsync(() => sheet["Repositories"].Items[0].ExecuteAsync("CloneCommand"));
        var cloned = await run.Ui.ReadAsync(() => page["Repository"].Text);
        await run.Ui.RunAsync(() =>
        {
            page.Set("Instruction", SimulatedRun.Simulate("edit"));

            return page.ExecuteAsync("SubmitCommand");
        });
        var job = Outcomes.Present(await run.Ui.ReadAsync(() => page["LastSubmitted"].Value<Option<JobId>>()));
        var settled = await run.SettledAsync(job);
        var approved = await run.Get<IJobs>().ApproveAsync(job, Cancellation);

        Assert.Equal(["shop"], listed);
        Assert.Equal(Path.Combine(projects.Path, "shop"), cloned);
        Assert.Equal([JobStatus.AwaitingReview], settled);
        Assert.True(approved.IsSuccess);
    }
}
