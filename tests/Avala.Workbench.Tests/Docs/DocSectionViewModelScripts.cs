using Avala.Documentation.Contracts;
using Avala.Workbench.Docs;

namespace Avala.Workbench.Tests.Docs;

public sealed class DocSectionViewModelScripts
{
    [Fact]
    public void ASectionShowsItsStepsTextAndKindAndChoosingItHandsUpItsAnchor()
    {
        var chosen = new List<string>();
        var section = new DocSectionViewModel(new PlayStep(StepKind.Changed, "payments", "Payments", "Checks a key first."), chosen.Add);

        section.ChooseCommand.Execute(null);
        section.Activate(true);

        Assert.Equal(("Payments", "Checks a key first.", "changed", true, true), (section.Title, section.Text, section.Mark, section.IsChanged, section.IsActive));
        Assert.Equal(["payments"], chosen);
    }
}
