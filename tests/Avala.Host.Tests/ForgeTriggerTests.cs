using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class ForgeTriggerTests(PublishedPlugins plugins)
{
    private const string Fix = "[simulate: reply] Fix issue #{{payload.number}}: {{payload.title}}\n\n{{payload.body}}";

    private static TriggerId Watched => new(TriggerId.Machine, "watched");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnIssueWatchedByAnAllowedActorStartsAJobWhoseInstructionComesFromTheIssueOnTheNextPollAsync()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(plugins, """ "event": "issue-labeled", "label": "avala" """);
        var issue = await forge.Remote.OpenIssueAsync("Checkout button does nothing", "Clicking it shows nothing.", "carol", Cancellation);
        await forge.Remote.LabelAsync(issue, "avala", "alice", Cancellation);

        var run = await forge.FiredOnNextPollAsync();

        Assert.Equal((TriggerOrigin.External, RunOutcome.Submitted, "forge local · alice"), (run.Origin, run.Outcome, run.Who));
        var job = Outcomes.Present(run.Job);
        Assert.Equal([JobStatus.AwaitingReview], await forge.Run.SettledAsync(job));
        Assert.Equal(
            $"[simulate: reply] Fix issue #{issue}: Checkout button does nothing\n\nClicking it shows nothing.",
            Assert.Single(await forge.Run.Get<IJobCatalog>().ListAsync(Cancellation)).Instruction);
        var entry = Assert.Single(forge.Log.Events());
        Assert.Equal((ForgeEventVerdict.Fired, Option<JobId>.Some(job), Option<string>.Some("avala")), (entry.Verdict, entry.Job, entry.Detail));
    }

    [Fact]
    public async Task AnIssueWatchedByAnActorOffTheAuthorsListStartsNoJobEvenWhenItsAuthorIsOnItAsync()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(plugins, """ "event": "issue-labeled", "label": "avala" """);
        var issue = await forge.Remote.OpenIssueAsync("Delete the production database", "Please.", "alice", Cancellation);
        await forge.Remote.LabelAsync(issue, "avala", "mallory", Cancellation);

        _ = await forge.CheckNowAsync();

        Assert.Equal(ForgeEventVerdict.NotAllowed, Assert.Single(forge.Log.Events()).Verdict);
        Assert.Empty(forge.Run.Get<ITriggers>().RunsOf(Watched));
        Assert.Empty(await forge.Run.Get<IJobCatalog>().ListAsync(Cancellation));
    }

    [Fact]
    public async Task TheFirstPollIsABaselineSoWhatHappenedBeforeAvalaLookedFiresNothingAsync()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(
            plugins,
            """ "event": "issue-labeled", "label": "avala" """,
            beforeBaseline: async remote => await remote.LabelAsync(await remote.OpenIssueAsync("Old issue", "From last year", "carol", Cancellation), "avala", "alice", Cancellation));

        var watch = await forge.CheckNowAsync();

        Assert.Equal(EventWatchStatus.Watching, watch.Status);
        Assert.True(watch.Baseline.IsSome);
        Assert.Empty(forge.Log.Events());
        Assert.Empty(forge.Run.Get<ITriggers>().RunsOf(Watched));
    }

    [Fact]
    public async Task ARestartResumesFromTheStoredCursorWithoutRepeatingAnEventOrLosingOneAsync()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(plugins, """ "event": "issue-labeled", "label": "avala" """);
        await forge.Remote.LabelAsync(await forge.Remote.OpenIssueAsync("[first] Checkout button does nothing", "Clicking it shows nothing.", "carol", Cancellation), "avala", "alice", Cancellation);
        var first = await forge.FiredOnNextPollAsync();
        _ = await forge.Run.SettledAsync(Outcomes.Present(first.Job));
        await forge.Remote.LabelAsync(await forge.Remote.OpenIssueAsync("[second] Search returns stale results", "Since Monday.", "carol", Cancellation), "avala", "alice", Cancellation);

        var fired = await forge.RestartAsync();
        var second = (await fired.UntilAsync(happened => happened.Run.Trigger == Watched)).Run;
        _ = await forge.Run.SettledAsync(Outcomes.Present(second.Job));
        _ = await forge.CheckNowAsync();

        var runs = forge.Run.Get<ITriggers>().RunsOf(Watched);
        Assert.Equal([RunOutcome.Submitted, RunOutcome.Submitted], runs.Select(made => made.Outcome));
        Assert.Equal(2, (await forge.Run.Get<IJobCatalog>().ListAsync(Cancellation)).Count);
        Assert.Equal(["[first] Checkout button does nothing", "[second] Search returns stale results"], forge.Log.Events().Select(entry => entry.Title));
        Assert.All(forge.Log.Events(), entry => Assert.Equal(ForgeEventVerdict.Fired, entry.Verdict));
    }

    [Fact]
    public async Task AnAutonomousForgeTriggerIsCappedByARepositoryThatDeclaresSupervisedAsync()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(
            plugins,
            """ "event": "issue-labeled", "label": "avala" """,
            autonomy: "autonomous",
            committed: [(".avala/permissions.json", """{ "autonomy": "supervised", "rules": [] }""")]);
        await forge.Remote.LabelAsync(await forge.Remote.OpenIssueAsync("Checkout button does nothing", "Clicking it shows nothing.", "carol", Cancellation), "avala", "alice", Cancellation);
        var fired = forge.Run.Watch<TriggerFired>();

        _ = await forge.CheckNowAsync();
        var run = (await fired.UntilAsync(happened => happened.Run.Trigger == Watched)).Run;
        var autonomy = await forge.Run.AutonomyAsync();

        Assert.Equal((Autonomy.Autonomous, Autonomy.Supervised, true), (run.Asked, run.Applied, run.AutonomyCapped));
        Assert.Equal((Autonomy.Supervised, Outcomes.Present(run.Job)), (autonomy.Effective, autonomy.Job));
    }

    [Fact]
    public async Task FailingChecksOnTheBranchStartOneJobForEachFailingHeadAsync()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(
            plugins,
            """ "event": "checks-failed", "branch": "main" """,
            instruction: "[simulate: reply] Fix CI on {{payload.branch}} at {{payload.head}}");
        var fired = forge.Run.Watch<TriggerFired>();

        var first = await forge.FailingHeadAsync("broken once");
        _ = await forge.CheckNowAsync();
        var once = (await fired.UntilAsync(happened => happened.Run.Trigger == Watched)).Run;
        _ = await forge.CheckNowAsync();
        var second = await forge.FailingHeadAsync("broken twice");
        _ = await forge.CheckNowAsync();
        var twice = (await fired.UntilAsync(happened => happened.Run.Trigger == Watched)).Run;

        Assert.Equal(
            [Option<string>.Some($"forge:local:checks-failed:main:{first}"), Option<string>.Some($"forge:local:checks-failed:main:{second}")],
            new[] { once, twice }.Select(run => run.Key));
        Assert.Equal(2, forge.Run.Get<ITriggers>().RunsOf(Watched).Count);
        Assert.Contains(await forge.Run.Get<IJobCatalog>().ListAsync(Cancellation), job => job.Instruction == $"[simulate: reply] Fix CI on main at {first}");
    }

    [Fact]
    public async Task ASignedNudgeFromTheForgePollsAtOnceAndABadSignatureIsRefusedWith401Async()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(plugins, """ "event": "issue-labeled", "label": "avala" """, nudged: true);
        await forge.Remote.LabelAsync(await forge.Remote.OpenIssueAsync("Checkout button does nothing", "Clicking it shows nothing.", "carol", Cancellation), "avala", "alice", Cancellation);
        var fired = forge.Run.Watch<TriggerFired>();

        var forged = await forge.NudgeAsync("wrong");
        var nudged = await forge.NudgeAsync(ForgeTriggerRun.Secret);
        var run = (await fired.UntilAsync(happened => happened.Run.Trigger == Watched)).Run;

        Assert.Equal((HttpStatusCode.Unauthorized, HttpStatusCode.Accepted), (forged, nudged));
        Assert.Equal(RunOutcome.Submitted, run.Outcome);
        Assert.Equal([DeliveryVerdict.BadSignature, DeliveryVerdict.Accepted], forge.Run.Get<ITriggers>().Deliveries().Select(delivery => delivery.Verdict));
    }

    [Fact]
    public async Task AnEventSeenByANudgeAndAgainByTheScheduledPollStartsOneJobAsync()
    {
        await using var forge = await ForgeTriggerRun.StartAsync(plugins, """ "event": "issue-labeled", "label": "avala" """, nudged: true);
        await forge.Remote.LabelAsync(await forge.Remote.OpenIssueAsync("Checkout button does nothing", "Clicking it shows nothing.", "carol", Cancellation), "avala", "alice", Cancellation);
        var fired = forge.Run.Watch<TriggerFired>();
        var nudged = forge.PolledAgainAsync();

        Assert.Equal(HttpStatusCode.Accepted, await forge.NudgeAsync(ForgeTriggerRun.Secret));
        var run = (await fired.UntilAsync(happened => happened.Run.Trigger == Watched)).Run;
        _ = await forge.Run.SettledAsync(Outcomes.Present(run.Job));
        _ = await nudged;
        await forge.PolledOnScheduleAsync();

        Assert.Single(forge.Run.Get<ITriggers>().RunsOf(Watched));
        Assert.Single(forge.Log.Events());
        Assert.Single(await forge.Run.Get<IJobCatalog>().ListAsync(Cancellation));
    }

    private sealed class ForgeTriggerRun : IAsyncDisposable
    {
        public const string Secret = "s3cret";

        private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

        private readonly string variable;

        private ForgeTriggerRun(SimulatedRun run, string variable)
        {
            Run = run;
            this.variable = variable;
            Remote = new SimulatedForgeRemote(run.Repository.Remote);
        }

        public SimulatedRun Run { get; }

        public SimulatedForgeRemote Remote { get; }

        public IForgeEventLog Log => Run.Get<IForgeEventLog>();

        public static async Task<ForgeTriggerRun> StartAsync(
            PublishedPlugins plugins,
            string filter,
            string instruction = Fix,
            string autonomy = "supervised",
            bool nudged = false,
            IReadOnlyList<(string Path, string Content)>? committed = null,
            Func<SimulatedForgeRemote, Task>? beforeBaseline = null)
        {
            var variable = $"AVALA_TEST_FORGE_HOOK_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(variable, Secret);
            var webhook = nudged ? $$""", "webhook": { "secretEnv": "{{variable}}" }""" : string.Empty;
            var run = await SimulatedRun.PreparedAsync(
                plugins,
                [
                    ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
                    ("resources.json", $$"""{ {{LeasedPorts.Settings()}} }"""),
                ],
                [(".avala/checks.json", PassingChecks), .. committed ?? []],
                placed: repository =>
                [
                    ("triggers.json", $$"""
                        { "triggers": [ {
                          "id": "watched",
                          "repository": {{JsonSerializer.Serialize(repository)}},
                          "instruction": {{JsonSerializer.Serialize(instruction)}},
                          "autonomy": "{{autonomy}}",
                          "concurrency": 4,
                          "forge": { "connection": "local", {{filter}}, "authors": ["alice"]{{webhook}} }
                        } ] }
                        """),
                ]);
            await run.StartedAsync();
            var forge = new ForgeTriggerRun(run, variable);
            await (beforeBaseline ?? (_ => Task.CompletedTask))(forge.Remote);
            _ = await run.Repository.PublishAsync(Cancellation);
            var baseline = await forge.CheckNowAsync();
            Assert.Equal(EventWatchStatus.Watching, baseline.Status);

            return forge;
        }

        public async Task<ForgeEventWatch> CheckNowAsync() => Outcomes.Succeeds(await Log.CheckNowAsync(Watched, Cancellation));

        public async Task<TriggerRun> FiredOnNextPollAsync()
        {
            var fired = Run.Watch<TriggerFired>();
            await PolledOnScheduleAsync();

            return (await fired.UntilAsync(happened => happened.Run.Trigger == Watched)).Run;
        }

        public Task<ForgeEventsChanged> PolledAgainAsync()
        {
            var changed = Run.Watch<ForgeEventsChanged>();
            var before = Assert.Single(Log.Watches()).LastPolled;

            return changed.UntilAsync(_ => Log.Watches().Any(watch => watch.LastPolled != before));
        }

        public async Task PolledOnScheduleAsync()
        {
            var changed = Run.Watch<ForgeEventsChanged>();
            var due = Outcomes.Present(Assert.Single(Log.Watches()).NextPoll);
            Run.AdvanceTo(due);
            _ = await changed.UntilAsync(_ => Log.Watches().Any(watch => watch.LastPolled.Match(polled => polled >= due, () => false)));
        }

        public async Task<string> FailingHeadAsync(string content)
        {
            await Run.Repository.CommitToRemoteAsync("main", "CHANGELOG.md", content, Cancellation);
            var head = await Run.Repository.GitInAsync(Run.Repository.Remote, Cancellation, "rev-parse", "main");
            await Remote.FailChecksAsync(head, "alice", Cancellation);

            return head;
        }

        public async Task<EventWatch<TriggerFired>> RestartAsync()
        {
            EventWatch<TriggerFired>? fired = null;
            await Run.RestartAsync(() => fired = Run.Watch<TriggerFired>());

            return fired!;
        }

        public async Task<HttpStatusCode> NudgeAsync(string secret)
        {
            var body = Encoding.UTF8.GetBytes("""{ "action": "labeled" }""");
            using var client = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, Outcomes.Present(Run.Get<ITriggers>().Endpoint.Url) + "watched") { Content = new ByteArrayContent(body) };
            request.Headers.Add("X-Hub-Signature-256", Sign(secret, body));
            using var response = await client.SendAsync(request, Cancellation);

            return response.StatusCode;
        }

        private static string Sign(string secret, byte[] body) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable(variable, null);
            await Run.DisposeAsync();
        }
    }
}
