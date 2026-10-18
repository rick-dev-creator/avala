using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Docs;
using Avala.Workbench.Documenting;
using Avala.Workbench.Following;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Workbench.Tests.Docs;

public sealed class DocsToggleViewModelScripts
{
    [Fact]
    public async Task TheGlyphAsksForTheShownJobsPaneAndFollowsWhetherItIsOpenAsync()
    {
        var messenger = new StrongReferenceMessenger();
        var job = JobId.New();
        var asked = new List<DocsPaneRequested>();
        messenger.Register<DocsPaneRequested>(asked, (list, message) => ((List<DocsPaneRequested>)list).Add(message));
        var board = new JobBoard();
        using var ui = new TestUiDispatcher();
        using var toggle = new DocsToggleViewModel(new DocsReader(board, new FakeDocumentation(), new FakeSitePreviews()), new LiveFeed(new Pulse(board), ui, NullLogger<LiveFeed>.Instance), messenger);
        toggle.OnRegionContextChanged(job);

        toggle.ToggleCommand.Execute(null);
        messenger.Send(new DocsPaneChanged(job, true));
        var open = toggle.IsOpen;
        messenger.Send(new DocsPaneChanged(JobId.New(), false));

        Assert.Equal([new DocsPaneRequested(job)], asked);
        Assert.True(open);
        Assert.True(toggle.IsOpen);
    }
}
