using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Linking;
using Avala.Workbench.Remoting;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Remoting;

public sealed class RemoteControlViewModelScripts : IDisposable
{
    private const string Link = "https://claude.ai/code/session_01XLwi39hWkad8C1AaNLEP69";

    private static readonly SessionId Session = SessionId.New();

    private readonly TestUiDispatcher ui = new();
    private readonly JobBoard board = new();
    private readonly RemoteKeeper keeper;
    private readonly RemoteJobs jobs;
    private readonly FakeClipboard clipboard = new();
    private readonly FakeLinks opener = new();
    private readonly JobSummary job = Pages.Summary("Ship the refunds flow", JobStatus.Running, "claude-work");

    public RemoteControlViewModelScripts()
    {
        keeper = new RemoteKeeper(board);
        jobs = new RemoteJobs(keeper);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobItsPersonStartedOnASessionThatOffersItCanBeContinuedOnThePhoneAsync()
    {
        await ShowsAsync(job, offered: true);
        using var remote = Remote(job);

        await ui.PresentedAsync(remote, () => remote.IsShown, () => remote.Status);

        Assert.Equal((false, false, RemotePhrases.Off), await ui.ReadAsync(() => (remote.IsOn, remote.HasLink, remote.Status)));
    }

    [Theory]
    [InlineData(true, true, JobStatus.Running)]
    [InlineData(false, false, JobStatus.Running)]
    [InlineData(false, true, JobStatus.Approved)]
    public async Task ADelegatedJobAJobWithoutRemoteControlOrAnEndedJobOffersNoPhoneAsync(bool delegated, bool offered, JobStatus status)
    {
        var shown = job with { Status = status, Parent = delegated ? JobId.New() : Option<JobId>.None };
        await ShowsAsync(shown, offered);
        using var remote = Remote(shown);

        await ui.PresentedAsync(remote, () => remote.Revision > 0, () => remote.Status);

        Assert.False(await ui.ReadAsync(() => remote.IsShown));
    }

    [Fact]
    public async Task TurningItOnShowsTheLinkAndItsCodeThenTheConnectedPhoneAsync()
    {
        await ShowsAsync(job, offered: true);
        using var remote = Remote(job);
        await ui.PresentedAsync(remote, () => remote.IsShown, () => remote.Status);

        await ui.ReadAsync(() => remote.ToggleCommand.ExecuteAsync(null));
        await keeper.HandleAsync(new RemoteControlReported(Session, new RemoteControl(RemoteState.Ready) { Link = Link }), Cancellation);
        await ui.PresentedAsync(remote, () => remote.HasLink, () => remote.Status);
        var waiting = await ui.ReadAsync(() => (remote.IsOn, remote.Link, remote.Code.IsEmpty, remote.Status, remote.IsConnected));
        await keeper.HandleAsync(new RemoteControlReported(Session, new RemoteControl(RemoteState.Connected) { Link = Link }), Cancellation);
        await ui.PresentedAsync(remote, () => remote.IsConnected, () => remote.Status);

        Assert.Equal([(job.Job, true)], jobs.Asked);
        Assert.Equal((true, Link, false, RemotePhrases.Waiting, false), waiting);
        Assert.Equal(RemotePhrases.Connected, await ui.ReadAsync(() => remote.Status));
    }

    [Fact]
    public async Task AHeldJobSaysItsRemoteControlIsPausedAsync()
    {
        var held = job with { Status = JobStatus.NeedsHelp, RemoteControlled = true };
        await ShowsAsync(held, offered: true);
        using var remote = Remote(held);

        await ui.PresentedAsync(remote, () => remote.IsShown, () => remote.Status);

        Assert.Equal((true, RemotePhrases.Paused), await ui.ReadAsync(() => (remote.IsOn, remote.Status)));
    }

    [Fact]
    public async Task ARefusalSaysWhyAsync()
    {
        await ShowsAsync(job, offered: true);
        jobs.Refusal = JobRejection.NotRemoteControllable;
        using var remote = Remote(job);
        await ui.PresentedAsync(remote, () => remote.IsShown, () => remote.Status);

        await ui.ReadAsync(() => remote.ToggleCommand.ExecuteAsync(null));

        await ui.PresentedAsync(remote, () => remote.HasProblem, () => remote.Problem);
        Assert.Equal(("This connection cannot be continued from a phone.", false), await ui.ReadAsync(() => (remote.Problem, remote.IsOn)));
    }

    [Fact]
    public async Task TheLinkCanBeCopiedOrOpenedAsync()
    {
        await ShowsAsync(job with { RemoteControlled = true }, offered: true);
        await keeper.HandleAsync(new RemoteControlReported(Session, new RemoteControl(RemoteState.Ready) { Link = Link }), Cancellation);
        using var remote = Remote(job);
        await ui.PresentedAsync(remote, () => remote.HasLink, () => remote.Status);

        await ui.ReadAsync(() => remote.CopyLinkCommand.ExecuteAsync(null));
        await ui.ReadAsync(() => remote.OpenLinkCommand.ExecuteAsync(null));

        Assert.Equal([Link], clipboard.Copied);
        Assert.Equal([new Uri(Link)], opener.Opened);
    }

    public void Dispose() => ui.Dispose();

    private async Task ShowsAsync(JobSummary shown, bool offered)
    {
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(shown.Job, new BoardJob(shown, Transcript.Empty)));
        var capabilities = offered ? CapabilitySet.Of(new ControlsRemotely()) : CapabilitySet.None;
        await keeper.HandleAsync(new SessionOpened(Session, new ProviderInfo("claude-code", "Claude Code"), "/work", new ConnectionName("claude-work")) { Capabilities = capabilities }, Cancellation);
        await keeper.HandleAsync(new JobSessionStarted(shown.Job, Session), Cancellation);
    }

    private RemoteControlViewModel Remote(JobSummary shown)
    {
        var remote = new RemoteControlViewModel(board, new LiveFeed(new Pulse(board), ui), jobs, new LinkTools(clipboard, new Links(opener, new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger())));
        remote.Activate();
        remote.OnRegionContextChanged(shown.Job);

        return remote;
    }

    private sealed class RemoteJobs(RemoteKeeper keeper) : IRemoteJobs
    {
        public List<(JobId Job, bool On)> Asked { get; } = [];

        public Option<JobRejection> Refusal { get; set; }

        public async ValueTask<Result<JobRemoteControlChosen, JobRejection>> ControlRemotelyAsync(JobId job, bool on, CancellationToken cancellationToken)
        {
            if (Refusal.Match(_ => true, () => false))
            {
                return Refusal.Match(rejection => rejection, () => JobRejection.UnknownJob);
            }

            Asked.Add((job, on));
            var chosen = new JobRemoteControlChosen(job, on);
            await keeper.HandleAsync(chosen, cancellationToken);

            return chosen;
        }
    }
}
