using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;
using Avala.Workbench.Harnesses;
using Avala.Workbench.Welcome;

namespace Avala.Workbench.Tests.Welcome;

public sealed class WelcomeHarnessScripts
{
    private static readonly OffersModels Large = new(["large", "small"], ["low", "medium", "high"]) { DefaultModel = "small", DefaultEffort = "medium" };

    private readonly FakeConnections connections = new FakeConnections("work", "personal").Automatic();
    private readonly FakePreview preview = new() { Answer = FakePreview.ByCapacity("personal", ChoiceReason.MostCapacity, ("work", 0.88, true), ("personal", 0.41, true)) };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheChipShowsWhatAutoWouldPickNowAndWhyAsync()
    {
        var harness = await ShownAsync();

        Assert.Equal((true, "Auto · personal", "P", false), (harness.Chip.IsShown, harness.Chip.Label, harness.Chip.Monogram, harness.Chip.IsAttention));
        Assert.Equal("Most capacity left · 41% of the 5-hour window used", harness.Chip.Reason);
    }

    [Fact]
    public async Task TheMenuOffersAutoAsRecommendedThenEachUsableConnectionWithItsUsageAsync()
    {
        connections.Catalog = connections.Catalog with
        {
            Connections = [.. connections.Catalog.Connections, new DeclaredConnection(new ConnectionName("broken"), "simulator", "login") { Problem = ConnectionError.MissingReference }],
        };
        preview.Answer = FakePreview.ByCapacity("personal", ChoiceReason.MostCapacity, ("work", 0.95, false), ("personal", 0.41, true));

        var harness = await ShownAsync();

        Assert.Equal(
            [("Auto", true, "Recommended · now personal", false, true), ("work", false, "95% of the 5-hour window used", true, false), ("personal", false, "41% of the 5-hour window used", false, false)],
            harness.Chip.Options.Select(option => (option.Name, option.IsAuto, option.Hint, option.IsAttention, option.IsChosen)));
    }

    [Fact]
    public async Task ChoosingAConnectionMarksItNamesItOnTheChipAndOffersItsModelsAsync()
    {
        connections.Offers["work"] = CapabilitySet.Of(Large);
        var harness = await ShownAsync();

        harness.Chip.Options[1].ChooseCommand.Execute(null);
        await harness.Offering;

        Assert.Equal([false, true, false], harness.Chip.Options.Select(option => option.IsChosen));
        Assert.Equal(("work", "W", "88% of the 5-hour window used"), (harness.Chip.Label, harness.Chip.Monogram, harness.Chip.Reason));
        Assert.True(harness.Chip.Models.CanChoose);
        Assert.Equal(["Default (small)", "large", "small"], harness.Chip.Models.Models);
    }

    [Fact]
    public async Task AChosenModelAndEffortShowOnTheChipAndTravelWithTheChoiceAsync()
    {
        connections.Offers["work"] = CapabilitySet.Of(Large);
        var harness = await ShownAsync();
        harness.Chip.Options[1].ChooseCommand.Execute(null);
        await harness.Offering;

        harness.Chip.Models.Model = "large";
        harness.Chip.Models.Effort = "high";

        Assert.Equal(("work · large · high", new ConnectionName("work"), new ModelChoice("large", "high")), (harness.Chip.Label, harness.Chosen.Match(name => name, () => new ConnectionName("none")), harness.Model));
    }

    [Fact]
    public async Task UnderAutoNoModelIsOfferedAndTheJobRunsWithTheDefaultOfTheConnectionAutoPicksAsync()
    {
        connections.Offers["personal"] = CapabilitySet.Of(Large);

        var harness = await ShownAsync();

        Assert.Equal((false, false, ModelChoice.Default), (harness.Chip.Models.IsShown, harness.Chip.Models.CanChoose, harness.Model));
    }

    [Fact]
    public async Task AChosenConnectionThatIsGoneFallsBackToAutoAsync()
    {
        var harness = await ShownAsync();
        harness.Chip.Options[1].ChooseCommand.Execute(null);
        await harness.Offering;
        connections.Catalog = connections.Catalog with { Connections = [.. connections.Catalog.Connections.Where(connection => connection.Name.Value != "work")] };

        await harness.ShowAsync(connections.Catalog, Cancellation);

        Assert.Equal(("Auto · personal", true), (harness.Chip.Label, harness.Chosen.IsNone));
        Assert.Equal(["Auto", "personal"], harness.Chip.Options.Select(option => option.Name));
    }

    [Fact]
    public async Task WhenEveryConnectionIsAtItsLimitTheChipTurnsAmberAndSaysSoAsync()
    {
        preview.Answer = FakePreview.ByCapacity("personal", ChoiceReason.AllAtLimit, ("work", 0.97, false), ("personal", 0.93, false));

        var harness = await ShownAsync();

        Assert.True(harness.Chip.IsAttention);
        Assert.StartsWith("All at their limit", harness.Chip.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFixedDefaultConnectionIsNamedDefaultAsync()
    {
        connections.Catalog = connections.Catalog with { DefaultMode = DefaultMode.Fixed, Default = new ConnectionName("work") };
        preview.Answer = new ConnectionPreview(ConnectionRoute.MachineDefault, new ConnectionName("work"));

        var harness = await ShownAsync();

        Assert.Equal(("Default · work", "The default connection of this machine", "Default"), (harness.Chip.Label, harness.Chip.Reason, harness.Chip.Options[0].Name));
    }

    internal static WelcomeHarness Harness(Submitting.JobLaunch launch) =>
        new(launch, new NewJobReadings(Pages.Readings(new FakeUsage())), new ModelPickerViewModel());

    private async Task<WelcomeHarness> ShownAsync()
    {
        var harness = Harness(Launching.Launch(new SubmittingJobs(), connections, preview, new FakePolicies()));
        await harness.ShowAsync(connections.Catalog, Cancellation);

        return harness;
    }
}

public sealed class HarnessOptionViewModelScripts
{
    [Fact]
    public void AnOptionReportsItsConnectionWhenChosenAndShowsItsUsageAndMark()
    {
        var chosen = new List<Option<ConnectionName>>();
        var option = new HarnessOptionViewModel("work", new ConnectionName("work"), chosen.Add);

        option.ChooseCommand.Execute(null);
        option.Show("95% of the 5-hour window used", attention: true, chosen: true);

        Assert.Equal([Option<ConnectionName>.Some(new ConnectionName("work"))], chosen);
        Assert.Equal(("W", false, "95% of the 5-hour window used", true, true), (option.Monogram, option.IsAuto, option.Hint, option.IsAttention, option.IsChosen));
    }
}
