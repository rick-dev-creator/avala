using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Resources.Contracts;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class PreviewTests(PublishedPlugins plugins)
{
    private const string ServicesPolicy = """{ "rules": [ { "name": "services", "kind": "command", "target": "dotnet *", "answer": "allow" } ] }""";

    private static readonly TimeSpan Sampling = TimeSpan.FromSeconds(0.2);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AServerAJobStartsOnItsLeasedPortBecomesItsPreviewAsync()
    {
        await using var run = await ServingAsync(test: 6);
        var available = run.Watch<PreviewAvailable>();
        var lease = await run.LeasedAsync();

        var target = await PreviewedAsync(run, available);

        Assert.Equal(
            (new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{lease.First}/")), PreviewSource.Server, true),
            (target.Address, target.Source, target.Leased));
    }

    [Fact]
    public async Task AJobsPreviewIsGoneOnceTheJobEndsAndItsServerIsReapedAsync()
    {
        await using var run = await ServingAsync(test: 7);
        var available = run.Watch<PreviewAvailable>();
        var gone = run.Watch<PreviewGone>();
        var target = await PreviewedAsync(run, available);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));

        Assert.Equal(target, (await gone.UntilAsync(left => left.Target.Job == run.Job)).Target);
        var server = Assert.Single((await run.OrphansFoundAsync()).Processes);
        Assert.True(await Workloads.IsGoneAsync(server.Id), $"Process {server.Id} survived its reaping");
    }

    [Fact]
    public async Task TheRunningChipOpensThePreviewBesideTheConversationThroughItsGatewayAndThePaneFollowsTheSelectionAsync()
    {
        await using var run = await ServingAsync(test: 6);
        var available = run.Watch<PreviewAvailable>();
        var workbench = await run.WorkbenchAsync();
        var lease = await run.LeasedAsync();
        _ = await PreviewedAsync(run, available);
        _ = await workbench.SelectAsync(run.Job);
        var regions = workbench.Page["Regions"];
        var chip = regions["Header"].Items.Single(item => item.Kind == "RunningChipViewModel");
        var pane = Assert.Single(regions["Preview"].Items);
        await run.Ui.PresentedAsync(chip.Presentation, () => chip["IsShown"].Value<bool>(), () => chip["Text"].Text);

        await run.Ui.InvokeAsync(() => chip.Execute("OpenCommand"), Cancellation);
        await run.Ui.PresentedAsync(pane.Presentation, () => run.Surfaces.Opened.Any(surface => surface.Visited.Count > 0), () => pane["State"].Text);

        var surface = Assert.Single(run.Surfaces.Opened);
        var gateway = Assert.Single(surface.Visited);
        Assert.Equal((true, true, $"localhost:{lease.First}"), await run.Ui.ReadAsync(() => (pane["IsOpen"].Value<bool>(), regions["IsPreviewOpen"].Value<bool>(), pane["Address"].Text)));
        Assert.NotEqual(lease.First, gateway.Port);
        using var client = new HttpClient();
        var page = await client.GetAsync(gateway, Cancellation);
        Assert.Contains($"Running on port {lease.First}", await page.Content.ReadAsStringAsync(Cancellation), StringComparison.Ordinal);
        Assert.True(page.Headers.Contains("Content-Security-Policy"));

        var other = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        _ = await workbench.SelectAsync(other);

        Assert.Equal((false, false), await run.Ui.ReadAsync(() => (pane["IsOpen"].Value<bool>(), regions["IsPreviewOpen"].Value<bool>())));
        Assert.True(surface.IsClosed);
    }

    private static async Task<PreviewTarget> PreviewedAsync(SimulatedRun run, EventWatch<PreviewAvailable> available)
    {
        await run.StartedAsync();
        var port = ServedPort(await run.TurnAsync());
        await run.DeliveredAsync();
        run.Clock.Advance(Sampling);
        var target = (await available.UntilAsync(seen => seen.Target.Job == run.Job && seen.Target.Address.Port == port)).Target;

        return target;
    }

    private Task<SimulatedRun> ServingAsync(int test) =>
        SimulatedRun.InstructedAsync(
            plugins,
            SimulatedRun.Simulate("processes"),
            [("resources.json", $$"""{ {{LeasedPorts.Settings(test)}}, "sampleSeconds": 0.2, "diskSeconds": 60 }""")],
            [(".avala/permissions.json", ServicesPolicy)]);

    private static int ServedPort(IReadOnlyList<IAgentEvent> turn) =>
        int.Parse(
            Assert.Single(turn.OfType<ItemProgressed>(), progressed => progressed.Item.Value == "serve").Text["listening ".Length..],
            CultureInfo.InvariantCulture);
}
