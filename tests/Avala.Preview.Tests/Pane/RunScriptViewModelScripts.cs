using Avala.Preview.Contracts;
using Avala.Preview.Pane;
using Avala.Testing;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Tests.Pane;

public sealed class RunScriptViewModelScripts : IDisposable
{
    private readonly PaneBench bench = new();

    [Fact]
    public async Task TheRunButtonNamesTheScriptItWouldRunAsync()
    {
        bench.Previews.Scripts[bench.Job] = "dev";

        var script = await SelectedAsync();

        script.Then(run => Assert.Equal((true, "Run dev", false), (run.IsShown, run.Label, run.IsRunning)));
    }

    [Fact]
    public async Task RunStartsTheScriptAsksForThePreviewAndOffersToStopItAsync()
    {
        bench.Previews.Scripts[bench.Job] = "dev";
        var asked = new List<PreviewRequested>();
        bench.Messenger.Register<List<PreviewRequested>, PreviewRequested>(asked, (list, message) => list.Add(message));
        var script = await SelectedAsync();

        await script.ViewModel.ToggleCommand.ExecuteAsync(null);

        script.Then(run => Assert.Equal(("Stop dev", true, false), (run.Label, run.IsRunning, run.HasProblem)));
        Assert.Equal([new PreviewRequested(bench.Job)], asked);
        Assert.True(bench.Previews.Running[bench.Job]);
    }

    [Fact]
    public async Task StopEndsTheRunAsync()
    {
        bench.Previews.Scripts[bench.Job] = "dev";
        bench.Previews.Running[bench.Job] = true;
        var script = await SelectedAsync();

        await script.ViewModel.ToggleCommand.ExecuteAsync(null);

        script.Then(run => Assert.Equal(("Run dev", false), (run.Label, run.IsRunning)));
    }

    [Fact]
    public async Task ARefusedRunSaysWhyAsync()
    {
        bench.Previews.Scripts[bench.Job] = "dev";
        bench.Previews.Refusal = PreviewError.NoLease;
        var script = await SelectedAsync();

        await script.ViewModel.ToggleCommand.ExecuteAsync(null);

        script.Then(run => Assert.Equal((true, "No free ports are left for this worktree.", false), (run.HasProblem, run.Problem, run.IsRunning)));
    }

    [Fact]
    public async Task AJobWithNothingToRunShowsNoButtonAsync()
    {
        var script = await SelectedAsync();

        script.Then(run => Assert.False(run.IsShown));
        Assert.False(script.CanInvoke(nameof(IRunScriptViewModel.ToggleCommand)));
    }

    [Fact]
    public async Task AJobThatServesAPageOffersToOpenItAsync()
    {
        bench.Previews.Scripts[bench.Job] = "index.html";

        (await SelectedAsync()).Then(run => Assert.Equal("Open index.html", run.Label));
    }

    public void Dispose() => bench.Dispose();

    private async Task<ViewModelScript<RunScriptViewModel>> SelectedAsync()
    {
        var run = bench.Run();
        run.Activate();
        run.OnRegionContextChanged(bench.Job);
        await run.Finding;

        return ViewModelScript.Given(run);
    }
}
