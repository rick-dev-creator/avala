using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Fleet.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class FleetTests(PublishedPlugins plugins)
{
    private static readonly (string File, string Content)[] Connected = [("fleet.json", FleetGate.Settings)];

    private static readonly TimeSpan Debounced = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan Retried = TimeSpan.FromSeconds(6);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobsFactsReachTheServerInOrderAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Connected, []);
        await run.StartedAsync();
        await run.AddRemoteAsync("origin", "git@github.com:Rick/Avala.git");
        var progressed = run.Watch<JobProgressed>();

        var job = await ReviewedAsync(run);
        var published = (await progressed.CollectUntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview)).Where(found => found.Job == job);
        run.Clock.Advance(Debounced);
        var events = await run.Fleet.Endpoint.EventsAsync(sent => sent.Any(found => found["status"]?.GetValue<string>() == "awaitingReview"), Cancellation);

        var ofJob = events.Where(found => found["job"]?.GetValue<string>() == job.Value.ToString()).ToList();
        var usage = ofJob.First(Kind("usageReported"));
        Assert.Equal(("jobSubmitted", "remote:github.com/rick/avala"), (ofJob[0]["kind"]!.GetValue<string>(), ofJob[0]["repository"]!.GetValue<string>()));
        Assert.Equal(
            string.Join(",", published.Select(found => JsonNamingPolicy.CamelCase.ConvertName(found.Status.ToString()))),
            string.Join(",", ofJob.Where(Kind("jobProgressed")).Select(found => found["status"]!.GetValue<string>())));
        Assert.Equal((1200L, 0.0042m), (usage["inputTokens"]!.GetValue<long>(), usage["cost"]!["amount"]!.GetValue<decimal>()));
        Assert.Contains(ofJob, found => found["kind"]?.GetValue<string>() == "attemptVerified");
        Assert.Equal($"Bearer {FleetGate.Key}", run.Fleet.Endpoint.Received.First(request => request.PathAndQuery == "/activity/v1/events").Authorization);
    }

    [Fact]
    public async Task FactsWaitWhileTheServerIsDownAndArriveLaterAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Connected, []);
        await run.StartedAsync();
        await ReviewedAsync(run);
        var before = run.Fleet.Endpoint.Events.Count;
        run.Fleet.Endpoint.Answering(_ => (HttpStatusCode.ServiceUnavailable, string.Empty));

        run.Clock.Advance(Debounced);
        var refused = (await run.Fleet.Endpoint.EventsAsync(sent => sent.Count > before, Cancellation)).Skip(before).Select(FakeFleetEndpoint.Id).ToList();
        var all = await run.Fleet.Endpoint.EventsAsync(sent => sent.Skip(before + refused.Count).Select(FakeFleetEndpoint.Id).ToHashSet().IsSupersetOf(refused), () => run.Clock.Advance(Retried), Cancellation);
        var later = all.Skip(before + refused.Count).Select(FakeFleetEndpoint.Id).ToList();

        Assert.Equal(later.Count, later.Distinct(StringComparer.Ordinal).Count());
        Assert.Subset(later.ToHashSet(StringComparer.Ordinal), refused.ToHashSet(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ADeferredFactIsSentAgainAndARejectedOneIsNotAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Connected, []);
        await run.StartedAsync();
        await ReviewedAsync(run);
        var before = run.Fleet.Endpoint.Events.Count;
        run.Fleet.Endpoint.Answering(sent => (
            HttpStatusCode.OK,
            FakeFleetEndpoint.Receipt(
                [.. sent.Skip(2).Select(FakeFleetEndpoint.Id)],
                [(FakeFleetEndpoint.Id(sent[0]), "unknown kind 'x'")],
                [(FakeFleetEndpoint.Id(sent[1]), "the job id is empty")])));

        run.Clock.Advance(Debounced);
        var first = (await run.Fleet.Endpoint.EventsAsync(sent => sent.Count > before, Cancellation)).Skip(before).ToList();
        var (deferred, rejected) = (FakeFleetEndpoint.Id(first[0]), FakeFleetEndpoint.Id(first[1]));
        var all = await run.Fleet.Endpoint.EventsAsync(sent => sent.Skip(before + first.Count).Any(found => FakeFleetEndpoint.Id(found) == deferred), () => run.Clock.Advance(Retried), Cancellation);
        var later = all.Skip(before + first.Count).Select(FakeFleetEndpoint.Id).ToList();

        Assert.Equal((1, 0), (later.Count(id => id == deferred), later.Count(id => id == rejected)));
    }

    [Fact]
    public async Task ARefusedKeyStopsSendingAndTheFleetSaysWhyAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Connected, []);
        await run.StartedAsync();
        run.Fleet.Endpoint.Answering(_ => (HttpStatusCode.Unauthorized, string.Empty));
        await ReviewedAsync(run);

        run.Clock.Advance(Debounced);
        _ = await run.Fleet.Endpoint.EventsAsync(sent => sent.Count > 0, Cancellation);
        await ReviewedAsync(run);
        run.Clock.Advance(TimeSpan.FromHours(1));
        await run.DeliveredAsync();
        var view = await run.Get<IFleetActivity>().RepositoriesAsync(Cancellation);

        Assert.Equal((1, FleetReadState.KeyRefused), (run.Fleet.Endpoint.Received.Count(request => request.PathAndQuery == "/activity/v1/events"), view.State));
        Assert.Contains("\"keyRefused\": true", await File.ReadAllTextAsync(Path.Combine(run.DataFolder, "fleet.json"), Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScratchJobSendsNothingAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Connected, []);
        await run.StartedAsync();
        var scratch = Outcomes.Succeeds(await run.Get<IScratchRepositories>().CreateAsync("sketch a parser", Cancellation));
        var progressed = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.Get<IJobs>().SubmitAsync(new JobRequest(scratch, SimulatedRun.Simulate("reply")), Cancellation));
        await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();
        run.Clock.Advance(TimeSpan.FromMinutes(1));
        await run.DeliveredAsync();

        Assert.DoesNotContain(run.Fleet.Endpoint.Events, found => found["job"]?.GetValue<string>() == job.Value.ToString());
    }

    [Fact]
    public async Task NothingIsSentBeforeConnectingAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);
        await run.StartedAsync();
        await ReviewedAsync(run);

        run.Clock.Advance(TimeSpan.FromMinutes(1));
        await run.DeliveredAsync();

        Assert.Empty(run.Fleet.Endpoint.Received);
    }

    [Fact]
    public async Task ConnectingThroughSettingsKeepsTheKeyOutOfFilesAndLogsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);
        await run.StartedAsync();
        run.Fleet.Keychain.Keys.Clear();
        var section = new Bound(run.Get<IEnumerable<RegionContribution>>().Single(contribution => contribution.ViewModel.GetType().Name == "FleetSettingsViewModel").ViewModel);
        section.Set("Server", FleetGate.Server);
        section.Set("KeyBox", FleetGate.Key);

        await section.ExecuteAsync("ConnectCommand");
        await ReviewedAsync(run);
        run.Clock.Advance(Debounced);
        _ = await run.Fleet.Endpoint.EventsAsync(sent => sent.Count > 0, Cancellation);
        var (keyBox, connected) = (section["KeyBox"].Text, section["IsConnected"].Value<bool>());
        var log = await run.StopAndReadLogAsync();

        Assert.Equal((FleetGate.Key, string.Empty, true, false), (run.Fleet.Keychain.Keys[FleetGate.Server], keyBox, connected, log.Contains(FleetGate.Key, StringComparison.Ordinal)));
        Assert.Empty(await FilesHoldingAsync(run.DataFolder, FleetGate.Key));
    }

    [Fact]
    public async Task TheFleetPageShowsTheServersRepositoriesAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Connected, []);
        run.Fleet.Endpoint.Repositories = """{"repositories":[{"key":"remote:github.com/rick/avala","jobs":2,"instances":2,"tokens":{"input":1500,"output":150,"cacheRead":9000,"cacheWrite":0,"reasoning":0},"costs":[{"currency":"USD","basis":"apiEquivalent","amount":0.75}],"firstActivityAt":"2026-10-25T11:00:00+00:00","lastActivityAt":"2026-10-25T12:00:00+00:00"}],"unattributedJobs":0}""";
        run.Fleet.Endpoint.Jobs = """{"jobs":[{"instance":"0199f6a0-0000-7000-8000-00000000000b","instanceName":"laptop","job":"0199f6a0-0000-7000-8000-000000000101","status":"running","hold":null,"attempts":0,"lastOutcome":null,"tokens":{"input":500,"output":50,"cacheRead":0,"cacheWrite":0,"reasoning":0},"costs":[],"firstActivityAt":"2026-10-25T11:30:00+00:00","lastActivityAt":"2026-10-25T11:30:00+00:00"}]}""";
        await run.StartedAsync();
        var page = run.Page("Fleet");

        await page.ExecuteAsync("RefreshCommand");
        await run.Ui.UntilAsync(() => page["Repositories"].Items.Count == 1, () => $"the page says '{page["Notice"].Text}' after {string.Join(", ", run.Fleet.Endpoint.Received.Select(request => request.PathAndQuery))}");
        var row = await run.Ui.ReadAsync(() => page["Repositories"].Items[0]);
        await row.ExecuteAsync("ToggleCommand");
        await run.Ui.UntilAsync(() => row["Jobs"].Items.Count == 1, () => "the repository's jobs did not show");

        Assert.Equal(
            ("remote:github.com/rick/avala", "laptop", "/activity/v1/repositories/jobs?key=remote%3Agithub.com%2Frick%2Favala"),
            (await run.Ui.ReadAsync(() => row["Key"]["Value"].Text), await run.Ui.ReadAsync(() => row["Jobs"].Items[0]["Instance"]["Value"].Text), run.Fleet.Endpoint.Received[^1].PathAndQuery));
    }

    private static Func<JsonNode, bool> Kind(string kind) => found => found["kind"]?.GetValue<string>() == kind;

    private static async Task<JobId> ReviewedAsync(SimulatedRun run)
    {
        var verified = run.Watch<AttemptVerified>();
        var progressed = run.Watch<JobProgressed>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        await verified.UntilAsync(found => found.Report.Job == job);
        await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        return job;
    }

    private static async Task<IReadOnlyList<string>> FilesHoldingAsync(string folder, string secret)
    {
        var needle = Encoding.UTF8.GetBytes(secret);
        List<string> holding = [];

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if ((await File.ReadAllBytesAsync(file, Cancellation)).AsSpan().IndexOf(needle) >= 0)
            {
                holding.Add(file);
            }
        }

        return holding;
    }
}
