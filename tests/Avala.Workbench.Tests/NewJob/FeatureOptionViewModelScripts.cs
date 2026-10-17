using Avala.Documentation.Contracts;
using Avala.Workbench.NewJob;

namespace Avala.Workbench.Tests.NewJob;

public sealed class FeatureOptionViewModelScripts
{
    [Fact]
    public void AnOptionNamesItsFeatureOrNotDocumentingAndChoosingItHandsItsChoiceUp()
    {
        var chosen = new List<FeatureChoice>();
        var feature = new FeatureOptionViewModel(FeatureChoice.Of(new Feature("refund-webhooks", "Refund webhooks")), FeaturePhrases.Suggested, chosen.Add);
        var none = new FeatureOptionViewModel(FeatureChoice.Undocumented, string.Empty, chosen.Add);

        feature.ChooseCommand.Execute(null);
        none.Mark(true);

        Assert.Equal(("Refund webhooks", true, false), (feature.Title, feature.IsNew, feature.IsNone));
        Assert.Equal((FeaturePhrases.DontDocument, false, true, true), (none.Title, none.IsNew, none.IsNone, none.IsChosen));
        Assert.Equal([feature.Choice], chosen);
    }
}
