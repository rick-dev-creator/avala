using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Machine;
using Avala.Workbench.Settings;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Settings;

public sealed class DefaultConnectionViewModelScripts
{
    private readonly FakeConnections connections = new FakeConnections("claude-work", "claude-personal").Automatic();
    private readonly IMessenger messenger = new StrongReferenceMessenger();

    [Fact]
    public void AutoIsOfferedFirstRecommendedAndChosenWhenTheMachineChoosesByCapacity() =>
        ViewModelScript.Given(Default())
            .When(chosen => chosen.Show(connections.Catalog))
            .Then(chosen =>
            {
                Assert.Equal([DefaultPhrases.Auto, "claude-work", "claude-personal"], chosen.Choices);
                Assert.Equal((DefaultPhrases.Auto, DefaultPhrases.Auto, false, true), (chosen.Draft, chosen.Saved, chosen.IsChanged, chosen.IsRecommended));
                Assert.StartsWith("Recommended. A job that names no connection", chosen.Explanation, StringComparison.Ordinal);
            });

    [Fact]
    public void AFixedDefaultIsChosenAndExplainedAsRunningThereEvenNearItsLimit()
    {
        var fixedOne = new FakeConnections("claude-work", "claude-personal");

        ViewModelScript.Given(Default())
            .When(chosen => chosen.Show(fixedOne.Catalog))
            .Then(chosen => Assert.Equal(
                ("claude-work", false, "A job that names no connection, in a repository that names none, runs on claude-work, even near its limit."),
                (chosen.Saved, chosen.IsRecommended, chosen.Explanation)));
    }

    [Fact]
    public void ChoosingAnotherDefaultOffersToSaveIt() =>
        ViewModelScript.Given(Shown())
            .When(chosen => chosen.Draft = "claude-personal")
            .ThenNotified(nameof(IDefaultConnectionViewModel.IsChanged), nameof(IDefaultConnectionViewModel.IsRecommended))
            .Then(chosen => Assert.Equal((true, false, true), (chosen.IsChanged, chosen.IsRecommended, chosen.SaveCommand.CanExecute(null))));

    [Fact]
    public async Task SavingAConnectionFixesTheDefaultAndTellsTheOtherPagesAsync()
    {
        var chosen = Shown();
        var told = new List<DefaultConnectionChanged>();
        messenger.Register<List<DefaultConnectionChanged>, DefaultConnectionChanged>(told, (list, message) => list.Add(message));
        ConnectionCatalog? reported = null;
        chosen.Changed += (_, catalog) => reported = catalog;

        chosen.Draft = "claude-personal";
        await chosen.SaveCommand.ExecuteAsync(null);

        Assert.Equal([Option<ConnectionName>.Some(new ConnectionName("claude-personal"))], connections.Changes);
        Assert.Equal(("claude-personal", false, string.Empty), (chosen.Saved, chosen.IsChanged, chosen.Error));
        Assert.Equal([new DefaultConnectionChanged(DefaultMode.Fixed, new ConnectionName("claude-personal"))], told);
        Assert.Equal(DefaultMode.Fixed, Assert.IsType<ConnectionCatalog>(reported).DefaultMode);
    }

    [Fact]
    public async Task SavingAutoReleasesAFixedDefaultAsync()
    {
        var fixedOne = new FakeConnections("claude-work", "claude-personal");
        var chosen = new DefaultConnectionViewModel(new MachineSettings(fixedOne, new FakeSupervision(), new FakeResources()), messenger);
        chosen.Show(fixedOne.Catalog);

        chosen.Draft = DefaultPhrases.Auto;
        await chosen.SaveCommand.ExecuteAsync(null);

        Assert.Equal([Option<ConnectionName>.None], fixedOne.Changes);
        Assert.Equal((DefaultPhrases.Auto, DefaultMode.Auto), (chosen.Saved, fixedOne.Catalog.DefaultMode));
    }

    [Theory]
    [InlineData("UnknownConnection", "That connection is no longer on this machine.")]
    [InlineData("Unwritable", "connections.json could not be written.")]
    [InlineData("Malformed", "connections.json is rejected (Malformed): edit the file to fix it.")]
    public async Task ARefusedChangeSaysWhyAndKeepsTheChoiceToTryAgainAsync(string refusal, string error)
    {
        var chosen = Shown();
        connections.Refusal = Enum.Parse<ConnectionError>(refusal);

        chosen.Draft = "claude-personal";
        await chosen.SaveCommand.ExecuteAsync(null);

        Assert.Equal((error, "claude-personal", DefaultPhrases.Auto, true), (chosen.Error, chosen.Draft, chosen.Saved, chosen.IsChanged));
    }

    [Fact]
    public void WithoutConnectionsOnlyAutoIsOffered()
    {
        var none = new FakeConnections().Automatic();

        ViewModelScript.Given(Default())
            .When(chosen => chosen.Show(none.Catalog))
            .Then(chosen => Assert.Equal(DefaultPhrases.Auto, Assert.Single(chosen.Choices)))
            .Then(chosen => Assert.Equal(DefaultPhrases.Auto, chosen.Draft));
    }

    [Fact]
    public async Task ADefaultWhoseConnectionIsGoneIsRepairedByChoosingAgainAsync()
    {
        connections.Catalog = new ConnectionCatalog(ConnectionFileStatus.Rejected, ConnectionError.UnknownDefault, [], Option<ConnectionName>.None);
        var chosen = Default();
        chosen.Show(connections.Catalog);
        var explained = chosen.Explanation;
        var offered = (chosen.Saved, chosen.Draft, chosen.IsChanged);
        connections.Catalog = new FakeConnections("claude-work").Catalog;

        await chosen.SaveCommand.ExecuteAsync(null);

        Assert.StartsWith("connections.json names a default connection this machine no longer has", explained, StringComparison.Ordinal);
        Assert.Equal((string.Empty, DefaultPhrases.Auto, true), offered);
        Assert.Equal([Option<ConnectionName>.None], connections.Changes);
        Assert.Equal((DefaultPhrases.Auto, string.Empty), (chosen.Saved, chosen.Error));
    }

    private DefaultConnectionViewModel Default() =>
        new(new MachineSettings(connections, new FakeSupervision(), new FakeResources()), messenger);

    private DefaultConnectionViewModel Shown()
    {
        var chosen = Default();
        chosen.Show(connections.Catalog);

        return chosen;
    }
}
