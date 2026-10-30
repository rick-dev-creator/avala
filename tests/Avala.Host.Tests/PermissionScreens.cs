using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

public sealed class PermissionScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ThePermissionScopeAndTheCardAfterARepositoryRuleAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "permission", (".avala/checks.json", PassingChecks));
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 1 && Card(conversation) is not null);

        await Camera.ShootAsync(run, "03-permission-card-scope-dark", window => FeatureCamera.ScrollTo(window, "Everything in this job"));
        await Camera.ShootAsync(run, "03-permission-card-scope-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "Everything in this job"));
        await run.Ui.InvokeAsync(() => workbench.Toolbar.Execute("ToggleDecisionsCommand"), Cancellation);
        await Camera.ShootAsync(run, "04-decisions-popover-scope-dark");
        await Camera.ShootAsync(run, "04-decisions-popover-scope-light", ThemeVariant.Light);
        await run.Ui.InvokeAsync(() => decisions["Items"].Items[0]["Scope"].Choose("ChooseCommand", "InRepository"), Cancellation);
        await Camera.ShootAsync(run, "05-decisions-popover-always-in-repository-chosen-dark", MoreChoices);

        await run.Ui.RunAsync(() => decisions.ExecuteAsync("AnswerCommand"));
        await workbench.ShowsAsync(() => Card(conversation) is { } card && !card["AwaitsYou"].Value<bool>());
        await run.Ui.InvokeAsync(() => workbench.Toolbar.Execute("CloseDecisionsCommand"), Cancellation);
        _ = await run.SettledAsync();
        await workbench.ShowsInGroupAsync(run.Job, "ReadyForReview");
        await Camera.ShootAsync(run, "06-permission-card-after-popover-always-in-repository-dark", window => FeatureCamera.ScrollTo(window, "dotnet ef database update"));

        await using var direct = await SimulatedRun.StartAsync(plugins, "permission", (".avala/checks.json", PassingChecks));
        _ = await direct.DecisionAsync();
        var bench = await direct.WorkbenchAsync();
        var talk = await bench.SelectAsync(direct.Job);
        await bench.ShowsAsync(() => Card(talk) is { } card && card["AwaitsYou"].Value<bool>());
        await direct.Ui.RunAsync(() =>
        {
            var card = Card(talk)!.Value;
            card["Scope"].Choose("ChooseCommand", "InRepository");

            return card.ExecuteAsync("AllowCommand");
        });
        await bench.ShowsAsync(() => Card(talk) is { } card && card["Notice"].Text.Length > 0);
        await Camera.ShootAsync(direct, "07-permission-card-added-to-permissions-json-dark", window => FeatureCamera.ScrollTo(window, "Added to .avala/permissions.json"));
        await Camera.ShootAsync(direct, "07-permission-card-added-to-permissions-json-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "Added to .avala/permissions.json"));
    }

    [Fact]
    public async Task AlwaysTheProgramEverythingInThisJobAndTheInspectorsFactAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "trusting", (".avala/checks.json", PassingChecks));
        var explored = await run.DecisionsAsync(count: 2);
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Card(conversation) is { } card && card["AwaitsYou"].Value<bool>());
        await run.Ui.InvokeAsync(() => Card(conversation)!.Value["Scope"].Choose("ChooseCommand", "Always"), Cancellation);

        await Camera.ShootAsync(run, "60-permission-card-always-the-program-dark", window => FeatureCamera.ScrollTo(window, "Always mkdir in this job"));
        await Camera.ShootAsync(run, "60-permission-card-always-the-program-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "Always mkdir in this job"));
        var decisions = await run.Ui.ReadAsync(() => workbench.Toolbar["Decisions"]);
        await run.Ui.InvokeAsync(() => workbench.Toolbar.Execute("ToggleDecisionsCommand"), Cancellation);
        await workbench.ShowsAsync(() => decisions["Items"].Items.Count == 1);
        await run.Ui.InvokeAsync(() => decisions["Items"].Items[0]["Scope"].Choose("ChooseCommand", "Always"), Cancellation);
        await Camera.ShootAsync(run, "61-decisions-popover-always-the-program-dark");
        await Camera.ShootAsync(run, "61-decisions-popover-always-the-program-light", ThemeVariant.Light);
        await run.Ui.RunAsync(() => decisions.ExecuteAsync("AnswerCommand"));
        await run.Ui.InvokeAsync(() => workbench.Toolbar.Execute("CloseDecisionsCommand"), Cancellation);

        var stamp = (await run.DecisionsAsync(count: 2))[1];
        await workbench.ShowsAsync(() => Waiting(conversation, stamp.Target) is not null);
        await run.Ui.InvokeAsync(() => Waiting(conversation, stamp.Target)!.Value["Scope"].Choose("ChooseCommand", "Everything"), Cancellation);
        await Camera.ShootAsync(run, "62-permission-card-everything-in-this-job-dark", window => FeatureCamera.ScrollTo(window, "Everything in this job"));
        await Camera.ShootAsync(run, "62-permission-card-everything-in-this-job-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "Everything in this job"));
        await run.Ui.RunAsync(() => Waiting(conversation, stamp.Target)!.Value.ExecuteAsync("AllowCommand"));

        Assert.Equal(PolicyAnswer.Allow, explored[0].Answer);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await workbench.ShowsInGroupAsync(run.Job, "ReadyForReview");
        _ = await workbench.InspectAsync(run.Job, "AutonomySectionViewModel");
        await Camera.ShootAsync(run, "63-inspector-everything-allowed-in-this-job-dark", FeatureCamera.Unfold("Autonomy & connection"));
        await Camera.ShootAsync(run, "63-inspector-everything-allowed-in-this-job-light", ThemeVariant.Light, FeatureCamera.Unfold("Autonomy & connection"));
    }

    [Fact]
    public async Task ARepositoryThatDeclaresSupervisedLocksEverythingInThisJobAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "permission", (".avala/checks.json", PassingChecks), (".avala/permissions.json", """{ "autonomy": "supervised" }"""));
        _ = await run.DecisionAsync();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Card(conversation) is { } card && card["AwaitsYou"].Value<bool>());

        await Camera.ShootAsync(run, "64-permission-card-repository-is-supervised-dark", window => FeatureCamera.ScrollTo(window, "repository is supervised"));
        await Camera.ShootAsync(run, "64-permission-card-repository-is-supervised-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "repository is supervised"));
    }

    [Fact]
    public async Task AFolderOutsideTheWorktreeAskedForOnceAndGrantedForTheJobAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "request-folder", (".avala/checks.json", PassingChecks), (".avala/permissions.json", """{ "autonomy": "autonomous" }"""));
        _ = await run.DecisionAsync();
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => Card(conversation) is { } card && card["AwaitsYou"].Value<bool>());

        await Camera.ShootAsync(run, "100-folder-card-asks-once-dark", window => FeatureCamera.ScrollTo(window, "Read and edit"));
        await Camera.ShootAsync(run, "100-folder-card-asks-once-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "Read and edit"));
        await run.Ui.RunAsync(() => Card(conversation)!.Value.ExecuteAsync("AllowCommand"));
        _ = await run.SettledAsync();
        await workbench.ShowsAsync(() => Card(conversation) is { } card && card["Notice"].Text.Length > 0);
        await Camera.ShootAsync(run, "101-folder-card-granted-for-the-job-dark", window => FeatureCamera.ScrollTo(window, "Updated the shared contracts."));
    }

    private static void Gated() =>
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");

    private static void MoreChoices(Window window)
    {
        foreach (var more in window.GetVisualDescendants().OfType<ToggleButton>().Where(toggle => toggle.Name == "More" && toggle.IsEffectivelyVisible).ToList())
        {
            more.IsChecked = true;
        }
    }

    private static Bound? Card(Bound conversation) =>
        conversation["Entries"].Items.Cast<Bound?>().FirstOrDefault(entry => entry!.Value.Kind == "PermissionCardViewModel");

    private static Bound? Waiting(Bound conversation, string target) =>
        conversation["Entries"].Items.Cast<Bound?>().FirstOrDefault(entry => entry!.Value.Kind == "PermissionCardViewModel" && entry.Value["Target"].Text == target && entry.Value["AwaitsYou"].Value<bool>());
}
