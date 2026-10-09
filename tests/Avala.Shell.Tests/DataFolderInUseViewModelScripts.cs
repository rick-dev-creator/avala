using Avala.Testing;

namespace Avala.Shell.Tests;

public sealed class DataFolderInUseViewModelScripts
{
    [Fact]
    public void ItNamesTheFolderSaysWhyItStopsAndAsksToQuit()
    {
        var quits = 0;
        var refusal = new DataFolderInUseViewModel("/data/avala");
        refusal.QuitRequested += (_, _) => quits++;

        ViewModelScript.Given(refusal)
            .Then(shown =>
            {
                Assert.Equal("/data/avala", shown.Folder);
                Assert.Contains("Another Avala already runs on this data folder", shown.Reason, StringComparison.Ordinal);
                Assert.Contains("AVALA_DATA_PATH", shown.Reason, StringComparison.Ordinal);
            })
            .Invoke("QuitCommand")
            .Then(_ => Assert.Equal(1, quits));
    }
}
