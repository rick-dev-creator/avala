using Avala.Testing;
using Avala.Workbench.Docs;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Docs;

public sealed class C4PlayerViewModelScripts
{
    [Fact]
    public void AStoryOpensOnItsLastVersionFullyBuiltWithItsSections()
    {
        using var player = Player(new FakeTimeProvider());

        Assert.Equal(["v1 Proposed", "v2 Deviation"], player.Versions);
        Assert.Equal(1, player.Version);
        Assert.Equal("Step 2 of 2 · Payments", player.Position);
        Assert.Equal(["Webhooks", "Payments"], player.Sections.Select(section => section.Title));
        Assert.True(player.Sections[1].IsActive);
        Assert.Contains(player.Nodes, node => node.Key == "webhooks" && node.IsNew);
    }

    [Fact]
    public void TheDocumentListsItsUnchangedSectionsAfterTheChangedOnesSoAnAsBuiltShowsThemAll()
    {
        var story = DesignStories.Ledger();
        var last = story.Versions[^1];
        var built = last with
        {
            Number = 3,
            Kind = Documentation.Contracts.VersionKind.AsBuilt,
            Steps = [],
            Document = last.Document with { Sections = [new("webhooks", "Webhooks", "Checks the signature."), new("payments", "Payments", "Applies the refund.")] },
        };
        using var player = new C4PlayerViewModel(new FakeTimeProvider(), new InlineUiDispatcher(), new Avala.Components.Zooming.ZoomViewModel());

        player.Show(story with { Versions = [.. story.Versions, built] });

        Assert.Equal([("Webhooks", "Checks the signature.", ""), ("Payments", "Applies the refund.", "")], player.Sections.Select(section => (section.Title, section.Text, section.Mark)));
    }

    [Theory]
    [InlineData(0, "No change to the diagram")]
    [InlineData(1, "1 step")]
    public void AtTheStartAVersionSaysHowManyStepsItPlaysOrThatItChangesNothing(int count, string position)
    {
        var story = DesignStories.Ledger();
        var last = story.Versions[^1];
        using var player = new C4PlayerViewModel(new FakeTimeProvider(), new InlineUiDispatcher(), new Avala.Components.Zooming.ZoomViewModel());
        player.Show(story with { Versions = [.. story.Versions, last with { Number = 3, Kind = Documentation.Contracts.VersionKind.AsBuilt, Steps = [.. last.Steps.Take(count)] }] });

        player.StopCommand.Execute(null);

        Assert.Equal(position, player.Position);
    }

    [Fact]
    public void BackNextAndStopMoveOneStepOrBackToTheStart()
    {
        using var player = Player(new FakeTimeProvider());

        player.BackCommand.Execute(null);
        var back = player.Position;
        player.StopCommand.Execute(null);
        var stopped = (player.Position, player.Nodes.Single(node => node.Key == "webhooks").IsHidden);
        player.NextCommand.Execute(null);

        Assert.Equal("Step 1 of 2 · Webhooks", back);
        Assert.Equal(("2 steps", true), stopped);
        Assert.Equal("Step 1 of 2 · Webhooks", player.Position);
    }

    [Fact]
    public void ChoosingASectionMovesTheDiagramToItsStep()
    {
        using var player = Player(new FakeTimeProvider());
        player.Version = 0;

        player.Shown.Single(section => section.Anchor == "payments").ChooseCommand.Execute(null);

        Assert.Equal("Step 3 of 6 · Payments", player.Position);
        Assert.Equal(["payments"], player.Sections.Where(section => section.IsActive).Select(section => section.Anchor));
        Assert.True(player.Nodes.Single(node => node.Key == "payments").IsFocus);
    }

    [Fact]
    public void PlayingAdvancesOnTimeHopByHopThroughAFlowAndStopsAtTheEnd()
    {
        var time = new FakeTimeProvider();
        using var player = Player(time);
        player.Version = 0;
        player.Shown.Single(section => section.Anchor == "postgres").ChooseCommand.Execute(null);

        player.PlayCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(1.9));
        var flow = (player.Position, player.Caption);
        time.Advance(TimeSpan.FromSeconds(1.4));
        var hop = player.Caption;
        time.Advance(TimeSpan.FromSeconds(1.4));
        time.Advance(TimeSpan.FromSeconds(1.4));
        time.Advance(TimeSpan.FromSeconds(1.4));
        var next = (player.Version, player.Position);
        time.Advance(TimeSpan.FromSeconds(1.9));
        var first = player.Position;
        time.Advance(TimeSpan.FromSeconds(1.9));
        time.Advance(TimeSpan.FromSeconds(1.9));

        Assert.Equal(("Step 6 of 6 · Flow: a charge", "1 · The merchant asks for a charge"), flow);
        Assert.Equal("2 · The API hands it to Payments", hop);
        Assert.Equal((1, "2 steps"), next);
        Assert.Equal("Step 1 of 2 · Webhooks", first);
        Assert.Equal(("Step 2 of 2 · Payments", false), (player.Position, player.IsPlaying));
    }

    [Fact]
    public void SpeedCyclesAndShortensThePauses()
    {
        var time = new FakeTimeProvider();
        using var player = Player(time);
        player.StopCommand.Execute(null);

        player.SpeedCommand.Execute(null);
        player.SpeedCommand.Execute(null);
        player.PlayCommand.Execute(null);
        time.Advance(TimeSpan.FromSeconds(0.95));

        Assert.Equal(("2×", "Step 1 of 2 · Webhooks"), (player.Speed, player.Position));
    }

    [Fact]
    public void AVersionWhoseMermaidThePlayerCannotReadShowsItAsMermaidDrawsItWithoutPlaying()
    {
        using var player = new C4PlayerViewModel(new FakeTimeProvider(), new InlineUiDispatcher(), new Avala.Components.Zooming.ZoomViewModel());
        var document = new Documentation.Contracts.VersionDocument("Refund webhooks", string.Empty, [], []) { Unread = ["flowchart LR\n  a --> b\n"] };
        var empty = new Documentation.Contracts.C4Diagram(Documentation.Contracts.C4Level.Container, string.Empty, [], [], []);

        player.Show(new Documentation.Contracts.FeatureStory(new Documentation.Contracts.Feature("refunds", "Refund webhooks"), [new Documentation.Contracts.PlayedVersion(1, Documentation.Contracts.VersionKind.Proposed, "v1-proposed.md", document, empty, [])]));

        var canvas = Assert.Single(player.Unread);
        Assert.Equal(("text/vnd.mermaid", "flowchart LR\n  a --> b\n", false), (canvas.MediaType, canvas.Content, player.IsDrawn));
    }

    [Fact]
    public void AnotherVersionIsFittedAgainWhileAStepKeepsTheZoom()
    {
        using var player = Player(new FakeTimeProvider());
        player.Zoom.ZoomInCommand.Execute(null);

        player.BackCommand.Execute(null);
        var stepped = player.Zoom.IsFitted;
        player.Version = 0;

        Assert.Equal((false, true), (stepped, player.Zoom.IsFitted));
    }

    private static C4PlayerViewModel Player(FakeTimeProvider time)
    {
        var player = new C4PlayerViewModel(time, new InlineUiDispatcher(), new Avala.Components.Zooming.ZoomViewModel());
        player.Show(DesignStories.Ledger());

        return player;
    }
}
