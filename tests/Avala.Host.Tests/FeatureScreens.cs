using System.Windows.Input;
using Avala.Autopilot.Contracts;
using Avala.Forges.Contracts;
using Avala.Notifications.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing.UI;
using Avala.Triggers.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

public sealed class FeatureScreens(HeadlessUi ui, PublishedPlugins plugins)
{
    private FeatureCamera Camera { get; } = new(ui, FeatureCamera.Folder ?? string.Empty);

    private static void Gated() =>
        Assert.SkipUnless(FeatureCamera.Folder is not null && HeadlessApp.Renders, $"Set {FeatureCamera.Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the feature screens.");

    [Fact]
    public async Task ThePlanPanelAndTheReviewsPlanProgressAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => conversation["Plan"]["IsShown"].Value<bool>() && ((ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.InvokeAsync(
            () =>
            {
                if (!conversation["Plan"]["IsExpanded"].Value<bool>())
                {
                    conversation["Plan"].Execute("ToggleCommand");
                }
            },
            TestContext.Current.CancellationToken);

        await Camera.ShootAsync(run, "01-plan-panel-expanded-dark");
        await Camera.ShootAsync(run, "01-plan-panel-expanded-light", ThemeVariant.Light);
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        await Camera.ShootAsync(run, "02-review-sheet-plan-progress-dark");
    }

    [Fact]
    public async Task TheNewJobModelAndEffortPickersAndTheModelAJobRanWithAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", TwoModelHarnesses)], [(".avala/checks.json", PassingChecks)]);
        var workbench = await run.WorkbenchAsync();
        var page = run.Page("New job");
        await run.Ui.RunAsync(() =>
        {
            ((ICommand)workbench.Toolbar["NewJobCommand"].Target).Execute(null);

            return page["Loading"].Value<Task>();
        });
        await run.Ui.RunAsync(async () =>
        {
            page.Set("Repository", run.Repository.Path);
            await page["Previewing"].Value<Task>();
            page.Set("Connection", "first");
            await page["Offering"].Value<Task>();
            page.Set("Instruction", "[simulate: reply] Summarise the release notes for the team");
        });

        var chosen = new[] { FeatureCamera.Pick("Model", "simulated-large"), FeatureCamera.Pick("Effort", "high") };
        await Camera.ShootAsync(run, "08-new-job-model-and-effort-dark", chosen);
        await Camera.ShootAsync(run, "08-new-job-model-and-effort-light", ThemeVariant.Light, chosen);
        await Camera.ShootAsync(run, "09-new-job-model-list-open-dark", [.. chosen, FeatureCamera.Open("Model")]);
        await Camera.ShootAsync(run, "09-new-job-effort-list-open-dark", [.. chosen, FeatureCamera.Open("Effort")]);
        await run.Ui.RunAsync(async () =>
        {
            page.Set("Connection", "second");
            await page["Offering"].Value<Task>();
        });
        await Camera.ShootAsync(run, "10-new-job-second-harness-models-open-dark", FeatureCamera.Open("Model"));
        await run.Ui.RunAsync(async () =>
        {
            page.Set("Connection", "first");
            await page["Offering"].Value<Task>();
            page["Models"].Set("Model", "simulated-large");
            page["Models"].Set("Effort", "high");
            await page.ExecuteAsync("SubmitCommand");
        });
        var job = Outcomes.Present(await run.Ui.ReadAsync(() => page["LastSubmitted"].Value<Option<JobId>>()));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));

        _ = await workbench.InspectAsync(job, "AutonomySectionViewModel");
        await Camera.ShootAsync(run, "11-conversation-header-and-inspector-ran-with-dark", FeatureCamera.Unfold("Autonomy & connection"));
    }

    [Fact]
    public async Task TheHandoffNoteTheInspectorsSpendPerConnectionAndAHeldJobWaitingForTheResetAsync()
    {
        Gated();
        (string, string)[] committed =
        [
            (".avala/checks.json", ReviewTests.CalculatorChecks),
            (".avala/permissions.json", ReviewTests.TestsPolicy),
            (".avala/jobs.json", """{ "limits": { "onLimit": "handoff-same-harness" } }"""),
        ];
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, committed);
        var progress = run.Watch<JobProgressed>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: limit-handoff] Fix the calculator")));
        _ = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.AwaitingReview);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        await workbench.ShowsInGroupAsync(job, "ReadyForReview");
        _ = await workbench.InspectAsync(job, "AutonomySectionViewModel");

        await Camera.ShootAsync(run, "12-handoff-note-and-inspector-spend-per-connection-dark", FeatureCamera.Unfold("Autonomy & connection"), window => FeatureCamera.ScrollTo(window, "Handed off to"));
        await Camera.ShootAsync(run, "12-handoff-note-and-inspector-spend-per-connection-light", ThemeVariant.Light, FeatureCamera.Unfold("Autonomy & connection"), window => FeatureCamera.ScrollTo(window, "Handed off to"));

        var waits = run.Watch<Avala.Handoffs.Contracts.JobWaitsForReset>();
        var held = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: limit-handoff] Fix the calculator on the first account") { Connection = new Avala.Agents.Contracts.Connections.ConnectionName("simulator-one") }));
        _ = await waits.UntilAsync(announced => announced.Wait.Job == held);
        _ = await workbench.SelectAsync(held);
        await workbench.ShowsInGroupAsync(held, "Waiting");
        await Camera.ShootAsync(run, "13-held-job-resumes-when-window-resets-dark", FeatureCamera.Unfold("Autonomy & connection"));
    }

    [Fact]
    public async Task AChildsQuestionWaitingOnItsParentAndAChildsPermissionGroupedUnderItInTheDecisionsAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", AsksParent));
        var asked = run.Watch<Avala.Delegation.Contracts.ParentAsked>();
        var workbench = await run.WorkbenchAsync();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-question-blocked] Choose the orders database")));
        var waiting = await asked.UntilAsync(_ => true);
        var conversation = await workbench.SelectAsync(parent);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "ChildAskingViewModel") is not null && ChildRow(workbench, Outcomes.Present(waiting.Delegation.Child)) is not null);

        await Camera.ShootAsync(run, "14-parent-conversation-while-subagent-waits-for-it-dark");

        await using var permission = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/jobs.json", AsksParent));
        var decided = permission.Watch<PermissionDecided>();
        var bench = await permission.WorkbenchAsync();
        var waits = Outcomes.Succeeds(await permission.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-waiting] Migrate the orders database")));
        _ = await decided.UntilAsync(update => update.Decision.Job != Option<JobId>.Some(waits) && update.Decision.Delivery == DecisionDelivery.LeftToHuman);
        var decisions = await permission.Ui.ReadAsync(() => bench.Toolbar["Decisions"]);
        await bench.ShowsAsync(() => decisions["Items"].Items.Count == 1);
        await permission.Ui.InvokeAsync(() => bench.Toolbar.Execute("ToggleDecisionsCommand"), Cancellation);
        await Camera.ShootAsync(permission, "15-decisions-popover-child-request-under-its-parent-dark");
    }

    [Fact]
    public async Task AChildsQuestionAnsweredByItsParentAndAReadOnlyReviewerChildAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", AsksParent));
        var decided = run.Watch<FormDecided>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-asks-parent] Choose the orders database")));
        var asked = (await decided.UntilAsync(update => update.Decision.Delivery == DecisionDelivery.LeftToParent)).Decision;
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(parent));
        var child = Outcomes.Present(asked.Job);
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(parent);
        var conversation = await SelectChildAsync(run, workbench, child);
        await workbench.ShowsAsync(() => FormCard(conversation) is { } card && card["Verdict"].Text.Contains("by its parent", StringComparison.Ordinal));
        await Camera.ShootAsync(run, "16-child-card-allowed-by-its-parent-dark", window => FeatureCamera.ScrollTo(window, "by its parent"));

        await using var review = await SimulatedRun.PreparedAsync(plugins, (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": {} }"""));
        var reported = review.Watch<Avala.Delegation.Contracts.ChildReported>();
        var reviewed = Outcomes.Succeeds(await review.SubmitAsync(new JobRequest(string.Empty, "[simulate: delegate-review] Review the release notes")));
        var record = (await reported.UntilAsync(_ => true)).Delegation;
        Assert.Equal([JobStatus.AwaitingReview], await review.SettledAsync(reviewed));
        var bench = await review.WorkbenchAsync();
        _ = await bench.InspectAsync(reviewed, "DelegationSectionViewModel");
        await Camera.ShootAsync(review, "17-reviewer-child-read-only-in-parent-inspector-dark", FeatureCamera.Unfold("Delegation"));
        _ = await SelectChildAsync(review, bench, Outcomes.Present(record.Report).Child);
        await Camera.ShootAsync(review, "18-reviewer-child-inspector-read-only-dark", FeatureCamera.Unfold("Autonomy & connection", "Decisions"));
    }

    [Fact]
    public async Task TheTriggersPageWithAScheduleAWebhookItsDeliveriesAndRunsAsync()
    {
        Gated();
        var variable = $"AVALA_SCREEN_HOOK_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "s3cret");
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("resources.json", $$"""{ {{LeasedPorts.Settings()}} }""")],
            [(".avala/checks.json", PassingChecks)],
            placed: repository =>
            [
                ("triggers.json", $$$"""
                    {
                      "triggers": [
                        { "id": "nightly-tests", "repository": {{{Json(repository)}}}, "schedule": { "everyMinutes": 60 }, "instruction": "[simulate: reply] Run the nightly checks and report" },
                        { "id": "issue-opened", "repository": {{{Json(repository)}}}, "webhook": { "secretEnv": "{{{variable}}}" }, "instruction": "{{payload.title}}" }
                      ]
                    }
                    """),
            ]);
        await run.StartedAsync();
        var triggers = run.Get<ITriggers>();
        var fired = run.Watch<TriggerFired>();
        run.Clock.Advance(TimeSpan.FromHours(1));
        var scheduled = (await fired.UntilAsync(_ => true)).Run;
        _ = await run.SettledAsync(Outcomes.Present(scheduled.Job));

        var url = Outcomes.Present(triggers.Endpoint.Url) + "issue-opened";
        var body = """{ "title": "[simulate: reply] Triage issue #42: checkout button does nothing" }""";
        using var client = new HttpClient();
        _ = await PostAsync(client, url, run, body, "n-1", "s3cret");
        var hooked = (await fired.UntilAsync(_ => true)).Run;
        _ = await PostAsync(client, url, run, body, "n-2", "wrong");
        _ = await PostAsync(client, url, run, body, "n-1", "s3cret");
        _ = await run.SettledAsync(Outcomes.Present(hooked.Job));
        _ = Outcomes.Succeeds(await triggers.FireAsync(new TriggerId(TriggerId.Machine, "nightly-tests"), new FireRequest(TriggerOrigin.Manual, "person"), Cancellation));

        _ = await run.WorkbenchAsync();
        var page = run.Page("Triggers");
        await run.Ui.InvokeAsync(() => run.Get<Avala.Shell.ShellViewModel>().SelectedPage = (Avala.Sdk.IPage)page.Target, Cancellation);
        await run.Ui.PresentedAsync(page.Presentation, () => page["Triggers"].Items.Count == 2 && page["Deliveries"].Items.Count == 3 && page["Triggers"].Items[0]["Runs"].Items.Count >= 2, () => "the triggers page did not show both triggers, three deliveries and the runs");
        await Camera.ShootAsync(run, "19-triggers-schedule-webhook-deliveries-runs-dark");
        await Camera.ShootAsync(run, "19-triggers-schedule-webhook-deliveries-runs-light", ThemeVariant.Light);
    }

    [Fact]
    public async Task TheTriggersPageWithALiveLoopAboveAnEndedOneAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            (".avala/checks.json", PassingChecks),
            (".avala/permissions.json", Autonomous),
            (".avala/jobs.json", """{ "approval": "merge", "autopilot": { "approve": "cleanEvidence" } }"""),
            (".avala/backlog.json", Backlog(("greet", "[simulate: edit] Greet the team"))));
        var autopilot = run.Get<IAutopilot>();
        var ended = run.Watch<LoopEnded>();
        var taken = run.Watch<LoopTaskTaken>();
        _ = Outcomes.Succeeds(await autopilot.StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        _ = await ended.UntilAsync(_ => true);
        await run.Repository.CommitAsync(
            ".avala/backlog.json",
            Backlog(("greet", "[simulate: edit] Greet the team"), ("checks", "[simulate: rewrite-checks] Loosen the checks"), ("docs", "[simulate: hang] Document the greeting")),
            Cancellation);

        var live = Outcomes.Succeeds(await autopilot.StartAsync(new LoopRequest(run.Repository.Path), Cancellation));
        _ = await taken.UntilAsync(task => task.Loop == live && task.Iteration == 2);

        _ = await run.WorkbenchAsync();
        var page = run.Page("Triggers");
        await run.Ui.InvokeAsync(() => run.Get<Avala.Shell.ShellViewModel>().SelectedPage = (Avala.Sdk.IPage)page.Target, Cancellation);
        await run.Ui.PresentedAsync(page.Presentation, () => page["Loops"]["Loops"].Items.Count == 2 && page["Loops"]["Loops"].Items[0]["Iterations"].Items.Count == 1, () => "the triggers page did not show the live loop with its iteration above the ended one");
        await Camera.ShootAsync(run, "26-triggers-live-loop-above-an-ended-one-dark");
        await Camera.ShootAsync(run, "26-triggers-live-loop-above-an-ended-one-light", ThemeVariant.Light);
    }

    [Fact]
    public async Task TheNotificationsSectionOfTheMachineSettingsAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("notifications.json", """{ "ntfy": { "topic": "avala-k3j9m2q7xw5rbt4na6ceyd8h" }, "quietHours": { "from": "22:00", "to": "07:00" } }""")],
            []);
        await run.StartedAsync();
        _ = await run.Get<INotifications>().SendTestAsync(Cancellation);

        _ = await run.WorkbenchAsync();
        var page = run.Page("Settings");
        await run.Ui.InvokeAsync(() => run.Get<Avala.Shell.ShellViewModel>().SelectedPage = (Avala.Sdk.IPage)page.Target, Cancellation);
        await run.Ui.RunAsync(() => page["Loading"].Value<Task>());
        Assert.True(await run.Ui.ReadAsync(() => page["Machine"]["Notifications"]["IsSetUp"].Value<bool>()));
        await run.Ui.InvokeAsync(() => page.ShowSection("Notifications"), Cancellation);

        await Camera.ShootAsync(run, "27-settings-notifications-dark");
        await Camera.ShootAsync(run, "27-settings-notifications-light", ThemeVariant.Light);
    }

    [Fact]

    [UsesRealGit]
    public async Task TheTriggersPageWithAForgeTriggerItsNextPollAndItsEventsWithTheirVerdictsAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("forges.json", """{ "pollSeconds": 300, "forges": [{ "name": "local", "forge": "simulated" }] }""")],
            [(".avala/checks.json", PassingChecks)],
            placed: repository =>
            [
                ("triggers.json", $$$"""
                    { "triggers": [ {
                      "id": "fix-labeled",
                      "repository": {{{Json(repository)}}},
                      "instruction": "Fix issue #{{payload.number}}: {{payload.title}}",
                      "concurrency": 2,
                      "forge": { "connection": "local", "event": "issue-labeled", "label": "avala", "authors": ["alice"] }
                    } ] }
                    """),
            ]);
        await run.StartedAsync();
        _ = await run.Repository.PublishAsync(Cancellation);
        var log = run.Get<IForgeEventLog>();
        var watched = new TriggerId(TriggerId.Machine, "fix-labeled");
        _ = Outcomes.Succeeds(await log.CheckNowAsync(watched, Cancellation));
        var remote = new SimulatedForgeRemote(run.Repository.Remote);
        var checkout = await remote.OpenIssueAsync("Checkout button does nothing", "Clicking it shows nothing.", "carol", Cancellation);
        await remote.LabelAsync(checkout, "bug", "bob", Cancellation);
        await remote.LabelAsync(await remote.OpenIssueAsync("Delete the production database", "Please.", "alice", Cancellation), "avala", "mallory", Cancellation);
        await remote.LabelAsync(checkout, "avala", "alice", Cancellation);
        var fired = run.Watch<TriggerFired>();
        _ = Outcomes.Succeeds(await log.CheckNowAsync(watched, Cancellation));
        _ = await run.SettledAsync(Outcomes.Present((await fired.UntilAsync(_ => true)).Run.Job));

        _ = await run.WorkbenchAsync();
        var page = run.Page("Triggers");
        await run.Ui.InvokeAsync(() => run.Get<Avala.Shell.ShellViewModel>().SelectedPage = (Avala.Sdk.IPage)page.Target, Cancellation);
        await run.Ui.PresentedAsync(page.Presentation, () => page["ForgeEvents"].Items.Count == 3 && page["Triggers"].Items.Count == 1, () => "the triggers page did not show the forge trigger and its three events");
        await Camera.ShootAsync(run, "28-triggers-forge-events-dark");
        await Camera.ShootAsync(run, "28-triggers-forge-events-light", ThemeVariant.Light);
    }

    private static string Backlog(params (string Id, string Instruction)[] tasks) =>
        $$"""{ "tasks": [ {{string.Join(", ", tasks.Select(task => $$"""{ "id": "{{task.Id}}", "instruction": "{{task.Instruction}}" }"""))}} ] }""";

    private static async Task<System.Net.HttpStatusCode> PostAsync(HttpClient client, string url, SimulatedRun run, string body, string nonce, string secret)
    {
        var timestamp = run.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bytes = System.Text.Encoding.UTF8.GetBytes(body);
        var signed = System.Text.Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.").Concat(bytes).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-Avala-Timestamp", timestamp);
        request.Headers.Add("X-Avala-Nonce", nonce);
        request.Headers.Add("X-Avala-Signature", Sign(secret, signed));
        using var response = await client.SendAsync(request, Cancellation);

        return response.StatusCode;
    }

    private static string Sign(string secret, byte[] signed) => "sha256=" + Convert.ToHexStringLower(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret), signed));

    private static string Json(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    [Fact]

    [UsesRealGit]
    public async Task ThePullRequestReviewButtonAndTheInspectorsWatchThroughAWakeUpAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }""")],
            [
                (".avala/checks.json", ReviewTests.CalculatorChecks),
                (".avala/jobs.json", """{ "approval": "pull-request", "pullRequest": { "forge": "local", "onPullRequest": "wake-on-ci", "maxWakeUps": 3, "redeliver": "automatic" } }"""),
                (".avala/permissions.json", Autonomous),
            ]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var changes = run.Watch<PullRequestWatchChanged>();
        var wakeUps = run.Watch<PullRequestWakeUp>();
        var opened = run.Watch<PullRequestOpened>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: fix-after-feedback] [forge: ci-fails-once] Fix the calculator")));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(job);
        await workbench.ShowsAsync(() => ((ICommand)workbench.Page["OpenReviewCommand"].Target).CanExecute(null));
        await run.Ui.RunAsync(() => workbench.Page.ExecuteAsync("OpenReviewCommand"));
        await Camera.ShootAsync(run, "20-review-sheet-open-pull-request-dark");
        await Camera.ShootAsync(run, "20-review-sheet-open-pull-request-light", ThemeVariant.Light);
        await run.Ui.InvokeAsync(() => workbench.Page.Execute("CloseReviewCommand"), Cancellation);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(job, Cancellation));
        _ = await opened.UntilAsync(found => found.Job == job);
        var armed = (await changes.UntilAsync(found => found.State.Job == job && found.State.Status == WatchStatus.Watching && found.State.NextPoll.IsSome)).State;
        var section = (await workbench.InspectAsync(job, "PullRequestSectionViewModel"))[0];
        await Camera.ShootAsync(run, "21-inspector-pull-request-watching-dark", FeatureCamera.Unfold("Pull request"));

        run.AdvanceTo(Outcomes.Present(armed.NextPoll));
        _ = await wakeUps.UntilAsync(found => found.WakeUp.Job == job);
        _ = await opened.UntilAsync(found => found.Job == job);
        var rearmed = (await changes.UntilAsync(found => found.State.Job == job && found.State.Status == WatchStatus.Watching && found.State.NextPoll.IsSome && found.State.Failures == 0 && found.State.WakeUps == 1)).State;
        await run.Ui.PresentedAsync(section.Presentation, () => section["Fact"].Text == "#1 · watching" && section["WakeUps"].Items.Count == 1, () => $"the pull request section shows {section["Fact"].Text}");
        await Camera.ShootAsync(run, "22-inspector-pull-request-checks-failed-agent-woken-dark", FeatureCamera.Unfold("Pull request"));

        run.AdvanceTo(Outcomes.Present(rearmed.NextPoll));
        _ = await changes.UntilAsync(found => found.State.Job == job && found.State.Status == WatchStatus.Ended);
        await run.Ui.PresentedAsync(section.Presentation, () => section["Fact"].Text == "#1 · checks passed", () => $"the pull request section shows {section["Fact"].Text}");
        await Camera.ShootAsync(run, "23-inspector-pull-request-green-after-one-wake-up-dark", FeatureCamera.Unfold("Pull request"));
        await Camera.ShootAsync(run, "23-inspector-pull-request-green-after-one-wake-up-light", ThemeVariant.Light, FeatureCamera.Unfold("Pull request"));
    }

    [Fact]
    public async Task TheOverviewWithSeveralConnectionsAndAgentsAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("connections.json", """
                {
                  "connections": [
                    { "name": "work", "provider": "simulator" },
                    { "name": "personal", "provider": "simulator" },
                    { "name": "second", "provider": "simulator-second" }
                  ]
                }
                """)],
            [(".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/jobs.json", """{ "delegation": { "connections": ["work", "personal"] } }""")]);
        var workbench = await run.WorkbenchAsync();
        var settled = new List<JobId>();

        foreach (var (instruction, connection) in new[]
        {
            ("[simulate: near-limit] Rewrite the importer for the new CSV format", "work"),
            ("[simulate: edit] Add a greeting to the README and run the tests", "personal"),
            ("[simulate: reply] Explain the rounding rules to the team", "second"),
            ("[simulate: delegate] Write the release notes and the to-do list", "work"),
        })
        {
            settled.Add(Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, instruction) { Connection = new Avala.Agents.Contracts.Connections.ConnectionName(connection) })));
        }

        _ = await run.SettledAsync([.. settled]);
        var running = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: hang] Profile the slow checkout endpoint") { Connection = new Avala.Agents.Contracts.Connections.ConnectionName("second") }));
        await workbench.ShowsInGroupAsync(running, "Running");
        var page = run.Page("Overview");
        await run.Ui.InvokeAsync(() => run.Get<Avala.Shell.ShellViewModel>().SelectedPage = (Avala.Sdk.IPage)page.Target, Cancellation);
        var connections = await run.Ui.ReadAsync(() => page["Connections"]);
        await run.Ui.PresentedAsync(connections.Presentation, () => connections["Connections"].Items.Count >= 3, () => $"{connections["Connections"].Items.Count} connection cards");
        await Camera.ShootAsync(run, "24-overview-connections-and-agents-dark");
        await Camera.ShootAsync(run, "24-overview-connections-and-agents-light", ThemeVariant.Light);

        await run.Ui.InvokeAsync(() => page.Execute("ShowDelegationCommand"), Cancellation);
        var delegation = await run.Ui.ReadAsync(() => page["Delegation"]);
        await run.Ui.InvokeAsync(() => delegation.Choose("ChooseScopeCommand", "All"), TestContext.Current.CancellationToken);
        await run.Ui.PresentedAsync(delegation.Presentation, () => delegation["Trees"].Items.Count >= 1, () => "no orchestrator in the delegation view");
        await Camera.ShootAsync(run, "25-overview-delegation-agents-tree-dark");
    }

    [Fact]
    public async Task TheInspectorsUsageHeadlineReadsWholeUnderACostCapAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "reply", (".avala/checks.json", PassingChecks), (".avala/permissions.json", Autonomous), (".avala/budget.json", """{ "costPerJob": { "USD": 1.20 } }"""));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        _ = await workbench.SelectAsync(run.Job);
        _ = await workbench.InspectAsync(run.Job, "UsageSectionViewModel");

        await Camera.ShootAsync(run, "26-inspector-usage-headline-under-a-cost-cap-dark", FeatureCamera.Unfold());
        await Camera.ShootAsync(run, "26-inspector-usage-headline-under-a-cost-cap-narrow-light", ThemeVariant.Light, FeatureCamera.Unfold(), window => window.Width = 900);
    }

    [Fact]
    public async Task TheDeniedCardOfAnAutonomousJobAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "tools", (".avala/permissions.json", Autonomous));
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(run.Job);
        await workbench.ShowsAsync(() => OpenWorkbench.Entry(conversation, "DeniedCardViewModel") is { } card && card["CanAllow"].Value<bool>());

        await Camera.ShootAsync(run, "52-denied-card-dark");
        await Camera.ShootAsync(run, "52-denied-card-light", ThemeVariant.Light);
    }

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private const string AsksParent = """{ "delegation": {} }""";

    private static async Task<Bound> SelectChildAsync(SimulatedRun run, OpenWorkbench workbench, JobId child)
    {
        await workbench.ShowsAsync(() => ChildRow(workbench, child) is not null);
        await run.Ui.InvokeAsync(() => workbench.Sidebar.Execute("SelectCommand", ChildRow(workbench, child)!.Value.Target), Cancellation);

        return await run.Ui.ReadAsync(() => workbench.Page["Conversation"]);
    }

    [Fact]
    public async Task TheNewJobRepositoryFieldSuggestsChoosesAndMakesRepositoriesAsync()
    {
        Gated();
        await using var run = await SimulatedRun.StartAsync(plugins, "plan-across-turns");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await using var notes = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(notes.Path, "ideas.md"), "# Ideas\n", Cancellation);
        var workbench = await run.WorkbenchAsync();
        var page = run.Page("New job");
        await run.Ui.RunAsync(() =>
        {
            ((ICommand)workbench.Toolbar["NewJobCommand"].Target).Execute(null);

            return page["Loading"].Value<Task>();
        });

        await Camera.ShootAsync(run, "40-new-job-repository-suggestions-dark", FeatureCamera.Open("RepositoryPath"));
        await Camera.ShootAsync(run, "40-new-job-repository-suggestions-light", ThemeVariant.Light, FeatureCamera.Open("RepositoryPath"));
        await run.Ui.RunAsync(async () =>
        {
            page.Set("Repository", notes.Path);
            await page["Chooser"]["Checking"].Value<Task>();
        });
        await Camera.ShootAsync(run, "41-new-job-not-a-repository-offer-dark");
        await Camera.ShootAsync(run, "41-new-job-not-a-repository-offer-light", ThemeVariant.Light);
        await run.Ui.RunAsync(() =>
        {
            var creating = page["Chooser"]["Creating"];
            creating.Set("Parent", Path.GetDirectoryName(notes.Path)!);
            creating.Set("Name", "invoices");
            creating.Set("IsOpen", true);

            return Task.CompletedTask;
        });
        await Camera.ShootAsync(run, "42-new-job-new-repository-dark");
        await Camera.ShootAsync(run, "42-new-job-new-repository-light", ThemeVariant.Light);
    }

    [Fact]
    public async Task StartingWithoutARepositoryAndTheScratchJobInTheSidebarAndInspectorAsync()
    {
        Gated();
        await using var run = await SimulatedRun.PreparedAsync(plugins);
        var workbench = await run.WorkbenchAsync();
        var page = run.Page("New job");
        await run.Ui.RunAsync(() =>
        {
            ((ICommand)workbench.Toolbar["NewJobCommand"].Target).Execute(null);

            return page["Loading"].Value<Task>();
        });
        await run.Ui.RunAsync(() =>
        {
            page.Set("Instruction", "[simulate: plan-across-turns] Sketch a CSV parser for bank exports");

            return Task.CompletedTask;
        });

        await Camera.ShootAsync(run, "50-new-job-start-without-a-repository-dark", window => FeatureCamera.ScrollTo(window, "No repository"));
        await Camera.ShootAsync(run, "50-new-job-start-without-a-repository-light", ThemeVariant.Light, window => FeatureCamera.ScrollTo(window, "No repository"));
        await run.Ui.RunAsync(() => page.ExecuteAsync("StartWithoutRepositoryCommand"));
        var job = Outcomes.Present(await run.Ui.ReadAsync(() => page["LastSubmitted"].Value<Option<JobId>>()));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        await workbench.ShowsInGroupAsync(job, "ReadyForReview");
        _ = await workbench.InspectAsync(job, "WorktreeSectionViewModel");

        await Camera.ShootAsync(run, "51-scratch-job-sidebar-and-inspector-dark", FeatureCamera.Unfold("Worktree"));
        await Camera.ShootAsync(run, "51-scratch-job-sidebar-and-inspector-light", ThemeVariant.Light, FeatureCamera.Unfold("Worktree"));
    }

    private static Bound? ChildRow(OpenWorkbench workbench, JobId child) =>
        SidebarRows.TopLevel(workbench.Sidebar)
            .SelectMany(row => row["Children"].Items)
            .Cast<Bound?>()
            .FirstOrDefault(row => row!.Value["Job"].Value<JobId>() == child);

    private static readonly (string File, string Content)[] TwoAccounts =
    [
        ("simulated-logins/one/.login", string.Empty),
        ("simulated-logins/two/.login", string.Empty),
    ];

    private const string TwoModelHarnesses = """
        {
          "connections": [
            { "name": "first", "provider": "simulator" },
            { "name": "second", "provider": "simulator-second" }
          ]
        }
        """;

    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static Bound? FormCard(Bound conversation) =>
        conversation["Entries"].Items.Cast<Bound?>().FirstOrDefault(entry => entry!.Value.Kind == "FormCardViewModel");
}
