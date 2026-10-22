using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class DeniedRetryTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    [Fact]
    public async Task AllowingADenialFromItsCardLetsTheAutonomousAgentRetryItAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "tools", (".avala/permissions.json", Autonomous));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.ResumableAsync();
        await run.DeliveredAsync();
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.AwaitingReview
            && OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is { } card && card["CanAllow"].Value<bool>());

        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel")!.Value.ExecuteAsync("AllowCommand"));

        var (denied, retried) = await run.DecisionsAsync(2) is [var first, var second] ? (first, second) : default;
        Assert.Equal(("https://example.com/style", PolicyAnswer.Deny), (denied.Target, denied.Answer));
        Assert.Equal(("https://example.com/style", PolicyAnswer.Allow, "always example.com in this job"), (retried.Target, retried.Answer, Outcomes.Present(retried.Rule).Name));
        Assert.Equal(["always example.com in this job"], run.Get<IPermissionAudit>().JobRulesOf(run.Job).Select(rule => rule.Name));
    }

    [Fact]
    public async Task AvalasNoticeThatSendsADocumentedJobBackAfterADenialIsAllowedIsNoChangeRequestOfThePersonAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "tools", (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "docs": { "enabled": true } }"""));
        var progress = run.Watch<JobProgressed>();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.ResumableAsync();
        await run.DeliveredAsync();
        await workbench.ShowsAsync(() =>
            conversation["Composer"]["Status"].Value<JobStatus>() == JobStatus.AwaitingReview
            && OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is { } card && card["CanAllow"].Value<bool>());

        await run.Ui.RunAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel")!.Value.ExecuteAsync("AllowCommand"));
        _ = await progress.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.Running);
        _ = await progress.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.AwaitingReview);

        await run.DeliveredAsync();

        Assert.Empty(run.Get<IDocumentation>().OfJob(run.Job).Match(documented => documented.Requests, () => []));
    }

    [Fact]
    public async Task ADeniedWriteOutsideTheWorktreeNamesThePathAndSaysWritingThereNeedsThePersonAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "outside-edit", (".avala/permissions.json", Autonomous));
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        var denied = (await run.DecisionsAsync(count: 2))[1];

        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is not null);

        Assert.Equal(
            ($"Edit {denied.Target}", $"{denied.Target} is outside the job's worktree, and writing there needs you."),
            await run.Ui.ReadAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is { } card ? (card["What"].Text, card["Why"].Text) : default));
    }
}
