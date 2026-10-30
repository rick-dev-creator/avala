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
        var view = await run.Get<IFleetActivity>().UsageAsync(new FleetUsageQuery(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26), FleetStep.Day, Option<string>.None), Cancellation);

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
    public async Task TheFleetPageShowsSpendOverTimeAndItsBreakdownsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Connected, []);
        run.Fleet.Endpoint.Usage = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "FleetWire", "v1", "responses", "usage.json"), Cancellation);
        await run.StartedAsync();
        var page = run.Page("Fleet");

        await page.ExecuteAsync("RefreshCommand");
        await run.Ui.UntilAsync(() => page["Tiles"].Items.Count == 6, () => $"the page says '{page["Notice"].Text}'");
        var asked = run.Fleet.Endpoint.Received[^1].PathAndQuery;
        var shown = await run.Ui.ReadAsync(() => (
            page["Tiles"].Items[0]["Value"].Text,
            page["Trend"]["Columns"].Items.Count,
            page["Lists"].Items[0]["Rows"].Items.Count,
            page["Lists"].Items[1]["Rows"].Items[0]["Name"]["Value"].Text));
        var repository = await run.Ui.ReadAsync(() => page["Lists"].Items[0]["Rows"].Items[0]);
        await repository.ExecuteAsync("ChooseCommand");
        await run.Ui.UntilAsync(() => page["IsScoped"].Value<bool>(), () => "the page was not scoped to the repository");

        Assert.Equal(("1.25 USD", 7, 3, "desktop"), shown);
        Assert.StartsWith("/activity/v1/usage?from=", asked, StringComparison.Ordinal);
        Assert.Contains("&step=day", asked, StringComparison.Ordinal);
        Assert.Equal(
            ["/activity/v1/usage", "/activity/v1/repositories/jobs"],
            run.Fleet.Endpoint.Received.TakeLast(2).Select(request => request.PathAndQuery.Split('?')[0]));
        Assert.Contains("repository=remote%3Agithub.com%2Frick%2Favala", run.Fleet.Endpoint.Received[^2].PathAndQuery, StringComparison.Ordinal);
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
