using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Workbench.NewJob;

namespace Avala.Workbench.Tests.NewJob;

public sealed class FeatureChipViewModelScripts
{
    private static readonly Feature Refunds = new("refund-webhooks", "Refund webhooks");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheChipShowsOnlyForARepositoryWithDocsOnAsync()
    {
        var documentation = new FakeDocumentation();
        var chip = new FeatureChipViewModel(documentation);

        await chip.ReadAsync("/repos/ledger", Cancellation);
        var off = chip.IsShown;
        documentation.Docs = new RepositoryDocs(true, "docs/features", []);
        await chip.ReadAsync("/repos/ledger", Cancellation);

        Assert.Equal((false, true), (off, chip.IsShown));
    }

    [Fact]
    public async Task TheChipHoldsTheSuggestionAsTheInstructionChangesWithTheRepositorysFeaturesAndNotDocumentingInItsMenuAsync()
    {
        var chip = await ShownAsync();

        chip.Follow("Add dark mode");
        var fresh = (chip.Name, chip.Fact);
        chip.Follow("Retry Refund webhooks Stripe sends twice");

        Assert.Equal(("Add dark mode", FeaturePhrases.New), fresh);
        Assert.Equal(("Refund webhooks", "v4 next"), (chip.Name, chip.Fact));
        Assert.Equal(["Refund webhooks", FeaturePhrases.DontDocument], chip.Options.Select(option => option.Title));
        Assert.Equal([true, false], chip.Options.Select(option => option.IsChosen));
    }

    [Fact]
    public async Task APickedFeatureStaysWhateverTheInstructionSaysAndIsKeptForTheJobThenTheChipFollowsAgainAsync()
    {
        var documentation = new FakeDocumentation { Docs = new RepositoryDocs(true, "docs/features", [new RepositoryFeature(Refunds, 3)]) };
        var chip = new FeatureChipViewModel(documentation);
        await chip.ReadAsync("/repos/ledger", Cancellation);
        chip.Follow("Add dark mode");
        chip.IsOpen = true;

        chip.Offered[1].ChooseCommand.Execute(null);
        chip.Follow("Add dark mode to the settings");
        var job = JobId.New();
        await chip.KeepAsync(job, Cancellation);
        chip.Follow("Fix a typo");

        Assert.Equal([(job, FeatureChoice.Of(Refunds))], documentation.Chosen);
        Assert.False(chip.IsOpen);
        Assert.Equal("Fix a typo", chip.Name);
    }

    [Fact]
    public async Task NotDocumentingTheJobIsKeptAsSuchAndADocsOffRepositoryKeepsNothingAsync()
    {
        var documentation = new FakeDocumentation { Docs = new RepositoryDocs(true, "docs/features", []) };
        var chip = new FeatureChipViewModel(documentation);
        await chip.ReadAsync("/repos/ledger", Cancellation);
        chip.Follow("Fix a typo");
        chip.Offered[^1].ChooseCommand.Execute(null);
        var shown = (chip.Name, chip.Fact);
        var job = JobId.New();

        await chip.KeepAsync(job, Cancellation);
        documentation.Docs = RepositoryDocs.Off;
        await chip.ReadAsync("/repos/ledger", Cancellation);
        await chip.KeepAsync(JobId.New(), Cancellation);

        Assert.Equal((FeaturePhrases.Undocumented, string.Empty), shown);
        Assert.Equal([(job, FeatureChoice.Undocumented)], documentation.Chosen);
    }

    private static async Task<FeatureChipViewModel> ShownAsync()
    {
        var chip = new FeatureChipViewModel(new FakeDocumentation { Docs = new RepositoryDocs(true, "docs/features", [new RepositoryFeature(Refunds, 3)]) });
        await chip.ReadAsync("/repos/ledger", Cancellation);

        return chip;
    }
}
