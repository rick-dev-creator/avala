using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Workbench.Tests.Following;

public sealed class ReadProblemViewModelScripts : IDisposable
{
    private readonly TestUiDispatcher ui = new();
    private readonly Pulse pulse = new(new JobBoard());
    private readonly LiveFeed feed;

    public ReadProblemViewModelScripts() => feed = new LiveFeed(pulse, ui, NullLogger<LiveFeed>.Instance);

    [Fact]
    public async Task AFeedThatCannotReadShowsTheProblemWithItsReasonAndAReadThatSucceedsHidesItAsync()
    {
        var problem = new ReadProblemViewModel(feed);
        var reads = 0;
        feed.Start(_ => Interlocked.Increment(ref reads) == 1 ? throw new IOException("The usage store is locked") : ValueTask.FromResult(reads), _ => { });
        await ui.PresentedAsync(feed, () => problem.IsShown, () => $"shown {problem.IsShown}");
        var failed = await ui.ReadAsync(() => (problem.IsShown, problem.Text));

        await pulse.HandleAsync(new UsageRecorded(SessionId.New(), Avala.Sdk.Option<JobId>.None), CancellationToken.None);

        await ui.PresentedAsync(feed, () => !problem.IsShown, () => $"shown {problem.IsShown}");
        Assert.Equal(((true, ReadProblemPhrases.Problem("The usage store is locked")), (false, string.Empty)), (failed, await ui.ReadAsync(() => (problem.IsShown, problem.Text))));
    }

    public void Dispose()
    {
        feed.Dispose();
        ui.Dispose();
    }
}
