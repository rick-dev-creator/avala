using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Playback;
using Avala.Simulator.Recordings;
using Avala.Testing;

namespace Avala.Simulator.Tests.Playback;

public sealed class ScenarioLibraryTests
{
    [Fact]
    public async Task SessionsThatPlayAContestTakeItsVariantsInTurnAsync()
    {
        await using var data = new TemporaryFolder();
        var library = new ScenarioLibrary(new RecordingFolder(new AvalaPaths(data.Path)));
        var options = new SessionOptions(data.Path, PermissionMode.AllowAll);
        var chosen = new List<string>();

        foreach (var _ in Enumerable.Range(0, 4))
        {
            chosen.Add((await library.ChooseAsync("[simulate: contest] Fix the calculator", options, TestContext.Current.CancellationToken)).Name);
        }

        Assert.Equal(["contest-fails", "contest-passes-big", "contest-passes-small", "contest-fails"], chosen);
    }
}
