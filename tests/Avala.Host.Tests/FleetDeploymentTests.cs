using Avala.Fleet.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class FleetDeploymentTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASimulatedJobReachesTheDeployedServerAndTheDashboardShowsItAsync()
    {
        var server = Environment.GetEnvironmentVariable("AVALA_FLEET_SERVER") ?? string.Empty;
        var key = Environment.GetEnvironmentVariable("AVALA_FLEET_KEY") ?? string.Empty;
        Assert.SkipUnless(server.Length > 0 && key.Length > 0, "Set AVALA_FLEET_SERVER and AVALA_FLEET_KEY to a deployed fleet server and an instance key; scripts/fleet-deploy-check.cs does.");
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], [], realFleet: true);
        await run.AddRemoteAsync("origin", "git@github.com:rick-dev-creator/fleet-deployment.git");
        await run.StartedAsync();
        var section = new Bound(run.Get<IEnumerable<RegionContribution>>().Single(contribution => contribution.ViewModel.GetType().Name == "FleetSettingsViewModel").ViewModel);

        try
        {
            await run.Ui.InvokeAsync(
                () =>
                {
                    section.Set("Server", server);
                    section.Set("KeyBox", key);
                },
                Cancellation);
            await run.Ui.RunAsync(() => section.ExecuteAsync("ConnectCommand"));
            await run.Ui.UntilAsync(() => section["IsConnected"].Value<bool>(), () => $"not connected: {section["Problem"].Text}");
            await run.Ui.InvokeAsync(() => ((IActivatable)section.Target).Activate(), Cancellation);

            var progressed = run.Watch<JobProgressed>();
            var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
            _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
            await run.DeliveredAsync();
            run.Clock.Advance(TimeSpan.FromSeconds(3));
            await run.Ui.UntilAsync(
                () => section["Waiting"].Text == "0 events waiting" && section["LastDelivery"].Text.StartsWith("Last delivered", StringComparison.Ordinal),
                () => $"not delivered: '{section["Waiting"].Text}', '{section["Unanswered"].Text}'");

            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid")).DateTime);
            var usage = await run.Get<IFleetActivity>().UsageAsync(
                new FleetUsageQuery(today.AddDays(-1), today.AddDays(2), FleetStep.Day, "remote:github.com/rick-dev-creator/fleet-deployment"),
                Cancellation);

            Assert.Equal((FleetReadState.Read, 1), (usage.State, usage.Usage.Totals.Jobs));
            Assert.True(usage.Usage.Totals.Tokens.Input > 0);
        }
        finally
        {
            await run.Ui.RunAsync(() => section.ExecuteAsync("DisconnectCommand"));
        }
    }
}
