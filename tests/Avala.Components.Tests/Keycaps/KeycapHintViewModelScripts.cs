using Avala.Components.Keycaps;
using Avala.Testing;

namespace Avala.Components.Tests.Keycaps;

public sealed class KeycapHintViewModelScripts
{
    [Fact]
    public void AHintPairsItsKeysWithTheirAction() =>
        ViewModelScript.Given(new KeycapHintViewModel("⇧⏎", "with a note"))
            .Then(hint => Assert.Equal(("⇧⏎", "with a note"), (hint.Keys, hint.Action)));

    [Fact]
    public void TheDesignTimeHintMovesThroughAList()
    {
        var hint = new DesignKeycapHintViewModel();

        Assert.Equal(("J K", "move"), (hint.Keys, hint.Action));
    }
}
